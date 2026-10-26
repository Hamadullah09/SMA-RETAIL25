# RFID in SMA RETAIL

SMA RETAIL reads UHF RFID tags with D2184 readers (R2000-based, "UHF RFID Reader Serial Interface Protocol V3.1",
TCP port 4001 by default). Tagged items ring up at the POS by themselves, next to barcode scanning, and each
reader's four antenna ports are given to POS stations by an administrator.

## How it fits together

```
 Readers (up to 63+ on one site)          RFID hub (one PC, or several)            POS stations
 ┌──────────────┐                         ┌──────────────────────────────┐        ┌───────────────────┐
 │ D2184 #1     │ ant 1..4 ── TCP 4001 ──▶│ SmaRetail.RfidHub            │  HTTP  │ SMA RETAIL app    │
 │ 192.168.0.178│                         │  • one async loop per reader │◀──────▶│ station 001       │
 ├──────────────┤        Ethernet         │  • antenna → station routing │  SSE   │  POS screen adds  │
 │ D2184 #2     │ ─────── switch ────────▶│  • RSSI filter, dedup        │───────▶│  tagged items     │
 │ 192.168.0.179│                         │  • admin commands            │        ├───────────────────┤
 ├──────────────┤                         │  /stations/{n}/events        │───────▶│ station 002 …     │
 │ …            │                         └──────────────┬───────────────┘        └───────────────────┘
 └──────────────┘                                        │ reads readers, antennas, assignments
                                                         ▼
                                                  SQL Server (RfidReaders, RfidAntennas, ItemTags)
```

* **One owner per reader.** A D2184 accepts one TCP client, so exactly one process talks to each reader: the RFID
  hub. Stations never connect to readers directly when there is a hub.
* **Antenna → station.** Each antenna port has a station number. The hub reads an antenna only while that station's
  POS screen is open (someone is listening), so idle counters do not transmit.
* **Connected from the moment the program opens.** The app starts RFID before the login window: every reader is
  connected straight away and kept connected: a keep-alive every 5 seconds notices a pulled cable, and a lost reader is
  tried again every 1 to 5 seconds, at once when Windows reports a network change (a cable plugged back in), so it is
  back by itself within seconds. The header shows an RFID chip (green: connected).
* **Quiet readers, one beep per piece.** Each connection sets the reader's own buzzer from its setting (Quiet by
  default; out of the box a D2184 beeps at every tag on the antenna, every round). The POS beeps once for each tag
  it rings up instead (*Setup › This station*), and warns when the computer's sound is muted.
* **Ways to run**, in *Setup › This station › RFID*:
  * **Automatic** (the default): if the shop has an RFID hub for all stations (*RFID readers › Hub for all
    stations…*), the station uses it; otherwise the app connects to the readers itself. On the first start with no
    readers set up it searches this PC's networks (the factory address 192.168.0.178 first, then every address of
    each local network on port 4001), registers what answers with its current settings, switches on the antenna
    ports that have an antenna (measured by return loss) and gives them to this station.
  * **This computer connects to the readers**: the same without the network search or the shop hub.
  * **Use the RFID hub on another computer**: the station listens to `SmaRetail.RfidHub` at the address given.
  * **Off.**
* **Settings live in the database.** Power per antenna, frequency region, link profile, buzzer, DRM, antenna
  detection and fast TID are edited in *RFID readers › Edit reader* and pushed with *Send settings to reader*, so a
  replaced reader is configured in one click.

## Setting up a site with many readers

1. **Network.** Connect every reader and the hub PC to the switch. Give each reader its own IP address in the same
   subnet (the vendor tool sets it; the default is 192.168.0.178, so number them .178, .179, … or plan a range such as
   192.168.0.101–163 for 63 readers). The hub PC needs an address in the same subnet.
2. **Hub.** On the PC on the switch, run `SmaRetail.RfidHub.exe` (or install it as a Windows service, below).
3. **Readers.** In *RFID readers* press *Find readers* (searches this PC's networks and adds every reader that
   answers), or add each reader by hand (name, where it is, IP address, port 4001; *Add reader* proposes the next free
   address).
4. **Antennas.** In each reader's *Antennas* tab (or the *Antennas and stations* grid), switch on the ports that have
   an antenna, name them ("Counter 3 mat"), set the power, and choose the station. One station may use several
   antennas, on any readers.
5. **Send settings.** Select the reader and *Send settings to reader*. The reader keeps them after power loss.
6. **Stations.** In *RFID readers › Hub for all stations…* enter `http://<hub PC>:5280` once; every station on
   *Automatic* then uses the hub when it starts. (Or set each station to *Use the RFID hub on another computer*.)
7. **Test.** *RFID readers › Live tag monitor* shows what a station's antennas read, with signal strength. Raise
   *Ignore tags weaker than* (Radio tab) or lower the antenna power if a counter picks up items from the next one.

### Running the hub as a Windows service

```
sc create SmaRetailRfidHub binPath= "C:\SMA RETAIL\RfidHub\SmaRetail.RfidHub.exe --connection \"Server=.\SQLEXPRESS;Database=SmaRetail;Trusted_Connection=True;TrustServerCertificate=True\"" start= auto
sc start SmaRetailRfidHub
```

Options: `--connection` (or `--localdb <name>` for development), `--hub <name>` (which readers this hub owns, default
`main`), `--urls` (default `http://0.0.0.0:5280`), `--apikey <secret>` (stations must then send the same key, set in
*Setup › This station › RFID hub key*). Allow TCP 5280 through the Windows firewall on the hub PC.

### Hub endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Up, number of readers, connected readers |
| GET | `/readers` | Live status of every reader (state, last error, tags read, active antennas, firmware) |
| GET | `/stations/{n}/events` | Server-sent events: one JSON tag read per `data:` line, for station *n* (used by the app) |
| GET (WebSocket) | `/stations/{n}/ws` | The same tag reads as WebSocket text messages, for browsers and other programs |
| POST | `/reload` | Re-read readers and assignments from the database (also done every minute) |
| GET | `/readers/{id}/info` | Read firmware, temperature, power, region, profile, DRM, detection, fast TID, GPIO |
| POST | `/readers/{id}/apply` | Write the stored settings to the reader |
| POST | `/readers/{id}/reset` | Restart the reader |
| POST | `/readers/{id}/return-loss?antenna=&frequency=` | Antenna return loss in dB |
| POST | `/readers/{id}/gpio?gpio=3\|4&high=true\|false` | Set an output (gate light, lock) |

## Tags and items

A tag (EPC) is matched to an item in this order:

1. **Registered tag**: *Inventory › item › RFID tags* lists the tags on that item (type the code, or *Read tags at
   this counter* to register everything the station's antenna sees for 10 seconds). Registered tags are tracked: a
   sale marks the tag *Sold*, a void or return puts it back *In stock*, and the POS refuses a sold tag unless
   *Return / refund* is on.
2. **GS1 SGTIN-96**: tags encoded from the product barcode (header 0x30) are decoded to the GTIN and matched to the
   item's stock code or supplier code (14-, 13- and 12-digit forms), so supplier-tagged goods need no registration.
3. **Item code as text**: tags whose EPC holds the stock code in ASCII.

Every piece carries its own tag and every tag belongs to one item, while the pieces share the item's stock code: seven
chocolates are one item with seven tags and a stock of seven. *Inventory* shows each item's number of tags, the
*RFID tags (EPC)* tab lists them with the label printed on each piece, and searching with *Epc* (or typing a full tag
code in *Any*) finds the item a tag belongs to.

At the counter each physical tag rings up once per sale. Pieces of the same item join one line: three chocolates on
the antenna are one line of quantity 3 holding the three tags (column *RFID*), and scanning the barcode again adds to
the line the same way. Serialized items, gift cards and weighed goods keep a line per piece. Lowering a line's
quantity or removing the line puts its tags aside, so they are not added again while they lie on the antenna, and tags
sold in the last ten minutes are ignored too. The sale keeps every tag of every line (`SaleLineTags`), so a void puts
all of them back in stock.

### Why TCP, server-sent events and WebSocket

The reader itself only speaks its own binary protocol over TCP (or serial), so whoever owns a reader talks TCP to it.
Between the hub and the stations, tag reads only flow one way, so the app uses server-sent events: plain HTTP,
reconnects by itself and passes proxies. The WebSocket endpoint carries the same data for clients that prefer it (a
browser dashboard, another system).

## Capacity and scaling

* The hub uses asynchronous sockets: 63 readers are 63 sockets and a handful of threads, not 63 threads. The test
  suite runs 63 simulated readers × 4 antennas routed to 252 stations at once.
* Each listener has a bounded queue (1,000 reads, oldest dropped), so a slow station never slows the readers.
* An offline reader is retried with back-off (1 s doubling to 5 s) without affecting the others; its problem shows
  in *RFID readers*.
* The same tag is reported to a station at most once per *Report the same tag again after* seconds (default 5).
* Larger sites split readers across several hubs with the reader's *Hub* name; each hub serves only its own readers.
* Tag lookups use the unique index on `ItemTags.Epc`; database contexts are pooled (`DataContext`) and SQL connections
  use SqlClient's pool.

## Protocol notes

The driver (`src/RetailPlus.Rfid/D2184`) was written from the vendor's protocol PDF and its `Reader` library:
frames `A0 Len Addr Cmd Data Check` (checksum = two's complement of the byte sum), real-time inventory `0x89` for one
antenna, fast antenna switching `0x8A` for several, per-antenna power `0x76`, frequency region `0x78`, link profile
`0x69` (the reader restarts, so it is only sent when it changes), buzzer `0x7A`, DRM `0x7C`, antenna detection
`0x62`, fast TID `0x8D`, temperature `0x7B`, GPIO `0x60/0x61`, return loss `0x7E`, reset `0x70`. RSSI in dBm is the
raw value − 129; frequency index 0–6 is 865–868 MHz and 7–59 is 902–928 MHz in 0.5 MHz steps.

Tested on a real D2184 (firmware 6.09): discovery, reading settings, return loss (18 dB on the connected port,
0 dB and error 0x22 on empty ports), real-time and fast-switch inventory, and tag reads streamed to a station over
both server-sent events and WebSocket.

A reader whose IP address is `sim` is simulated (no hardware); demo tags go in its *Notes*, one `antenna:EPC` per
line. The tests and the app's `--smoke` check use this.
