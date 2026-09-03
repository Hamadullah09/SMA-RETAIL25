import { ROUTES } from './routes';

/**
 * The guides, keyed by the topic a route resolves to.
 *
 * Written for somebody who is not technical and is probably in a hurry. Short numbered steps, the
 * words that are on the screen, and no jargon that the screen does not itself use.
 *
 * Every topic the route registry names now has one, and each was written from the screen itself —
 * the button labels here are the words actually on those buttons ("Post — this moves stock",
 * "Ship — take the stock off the shelf", "Receive what was typed"), because a guide that renames
 * things is a guide somebody has to translate while they are already stuck.
 *
 * The fallback below stays for a topic added later and not yet written. It says so plainly rather
 * than inventing instructions: on a system that moves stock and money, made-up guidance is worse
 * than none, because somebody will follow it confidently.
 *
 * What is deliberately not here is anything about *why* a business rule exists. This answers "how
 * do I do the thing on this screen", and stops.
 */
export interface HelpSection {
  heading: string;
  /** Prose paragraphs. */
  body?: string[];
  /** Numbered steps, for a task. */
  steps?: string[];
  /** Term-and-meaning pairs, for statuses and colours. */
  definitions?: Array<{ term: string; meaning: string }>;
}

export interface HelpTopic {
  slug: string;
  title: string;
  summary: string;
  sections: HelpSection[];
}

export const HELP_TOPICS: Record<string, HelpTopic> = {
  pos: {
    slug: 'pos',
    title: 'Point of Sale',
    summary: 'Ringing up a sale at the till.',
    sections: [
      {
        heading: 'Selling something',
        steps: [
          'Scan the barcode, or type the code into the box at the top and press Enter.',
          'The item appears in the sale on the left. Scan the next one.',
          'When everything is on the list, press F4 or the Pay button.',
          'Choose how they are paying, type the amount handed over, and confirm.',
          'The change owed is shown at the top of the screen with the sale number.',
        ],
      },
      {
        heading: 'The keys along the bottom',
        definitions: [
          { term: 'F4 Pay', meaning: 'Take payment for the sale on screen.' },
          { term: 'F5 Client', meaning: 'Attach a customer, so the sale counts towards their account and points.' },
          { term: 'F6 Delete', meaning: 'Remove the last line from the sale.' },
          { term: 'F7 Reprint', meaning: 'Print the last receipt again.' },
          { term: 'F8 Credits', meaning: 'Gift cards, credit notes and refunds.' },
          { term: 'F9 Find', meaning: 'Search for an item by name instead of scanning.' },
          { term: 'F10 Drawer', meaning: 'Open the cash drawer without making a sale.' },
          { term: 'F11 Special', meaning: 'Holds, discounts and the less common actions.' },
          { term: 'Ctrl+G', meaning: 'Show or hide the picture grid of items.' },
        ],
      },
      {
        heading: 'The badges along the top',
        body: [
          'These show whether the till hardware is working. Each one has a shape and a word as well as a colour, so they can be read without relying on colour alone.',
        ],
        definitions: [
          { term: 'Green, ticked', meaning: 'Working.' },
          { term: 'Amber, triangle', meaning: 'Working, but something needs attention.' },
          { term: 'Red, cross', meaning: 'Not working. The printer or scanner will not respond.' },
        ],
      },
      {
        heading: 'If something goes wrong',
        body: [
          'A message in red under the scan box means the item was not recognised. Check the code and try again, or use F9 to search by name.',
          'If the screen says the till is not configured, the machine has not been told which shop and till it is. An administrator sets that up.',
        ],
      },
    ],
  },

  dashboard: {
    slug: 'dashboard',
    title: 'Dashboard',
    summary: 'What is happening in the shop today.',
    sections: [
      {
        heading: 'The tiles across the top',
        definitions: [
          { term: 'Sales today', meaning: "Money taken today, after refunds, and how many sales that was." },
          { term: 'Last 14 days', meaning: 'The same figure over the last fortnight.' },
          { term: 'Below reorder', meaning: 'How many products have fallen to or below the level you said to reorder at.' },
          { term: 'Owed to you', meaning: 'Money customers still owe on account.' },
        ],
      },
      {
        heading: 'Hiding the takings',
        body: [
          'The Show takings button hides and shows every money figure on this screen, including the charts. It starts hidden, and it remembers your choice on this computer only — so the office machine can show them while the one facing the shop floor does not.',
        ],
      },
    ],
  },

  products: {
    slug: 'products',
    title: 'Inventory',
    summary: 'The things you sell — names, prices, barcodes and RFID tags.',
    sections: [
      {
        heading: 'Finding a product',
        steps: [
          'Type any part of the name, the code or the barcode into the search box.',
          'Click a row to open it. Its details appear beside the list.',
          'Change what you need and press Save.',
        ],
      },
      {
        heading: 'Stock colours',
        body: ['Stock is shown as a number with a word beside it, never colour on its own.'],
        definitions: [
          { term: 'In stock', meaning: 'Above the reorder level.' },
          { term: 'Low stock', meaning: 'At or below the level you said to reorder at.' },
          { term: 'Out of stock', meaning: 'None on hand.' },
        ],
      },
    ],
  },

  sales: {
    slug: 'sales',
    title: 'Previous sales',
    summary: 'Looking up a sale that has already been rung up.',
    sections: [
      {
        heading: 'Finding a sale',
        steps: [
          'Choose the dates you want to look between.',
          'Type a sale number, a customer name or an item to narrow it down.',
          'Click a sale to see what was on it.',
        ],
      },
      {
        heading: 'Refunds and reprints',
        body: [
          'Open the sale first, then choose the refund or reprint action from it. Refunding from the original sale is what keeps the stock and the takings correct — a refund rung up from scratch does not know what was sold.',
        ],
      },
    ],
  },

  reports: {
    slug: 'reports',
    title: 'Reports',
    summary: 'Figures for a period you choose.',
    sections: [
      {
        heading: 'Running a report',
        steps: [
          'Pick the report you want from the list.',
          'Choose the dates at the top.',
          'The figures appear underneath. Export CSV saves them for a spreadsheet.',
        ],
      },
      {
        heading: 'If the figures look wrong',
        body: [
          'Check the dates first — a report always covers the period shown at the top, not today.',
          'Costs and margins are only shown to accounts allowed to see them, so two people can correctly see different columns in the same report.',
        ],
      },
    ],
  },

  customers: {
    slug: 'customers',
    title: 'Customers',
    summary: 'Customer records, balances and loyalty points.',
    sections: [
      {
        heading: 'Adding a customer',
        steps: [
          'Press Add.',
          'Fill in at least a name. Everything else can be added later.',
          'Press Save. They can now be attached to a sale with F5 at the till.',
        ],
      },
    ],
  },
  inventory: {
    slug: 'inventory',
    title: 'Stock',
    summary: 'How many of each thing you have, and putting that right.',
    sections: [
      {
        heading: 'What this screen is',
        body: [
          'Products is the list of things you sell. Stock is how many of each you have. They are two screens because they answer two different questions, and most days you only need this one.',
        ],
      },
      {
        heading: 'Booking in a delivery',
        steps: [
          'Press Receive.',
          'Find the item, and type how many arrived.',
          'Save. The stock goes up straight away and the movement is recorded.',
        ],
      },
      {
        heading: 'Correcting a number',
        steps: [
          'Press Adjust.',
          'Type what is actually on the shelf.',
          'Give a reason — shrinkage, damage, a miscount. The reason is what makes the figure explainable later.',
        ],
        body: [
          'Use Adjust for a correction and Receive for a delivery. Both change the number; only one of them means stock arrived.',
        ],
      },
      {
        heading: 'Finding what needs ordering',
        body: [
          'Tick "At or below reorder point" to see only the items that have fallen to the level you said to reorder at. That list is what a purchase order is built from.',
        ],
      },
    ],
  },

  counts: {
    slug: 'counts',
    title: 'Stock counts',
    summary: 'Counting the shelves and making the system agree with them.',
    sections: [
      {
        heading: 'Doing a count',
        steps: [
          'Start a count and choose what it covers.',
          'Press "Download the sheet" to get a list to carry round, or count on screen.',
          'Type in what you actually found. Lines you do not touch are left alone.',
          'Press "Post — this moves stock".',
        ],
      },
      {
        heading: 'What posting does',
        body: [
          'Every line you counted is written to the stock ledger, so what is on the shelf becomes what the system believes. Lines you did not count are not changed. It cannot be undone — if a figure was wrong, correct it afterwards with an adjustment, which leaves a reason behind.',
        ],
      },
      {
        heading: 'If you change your mind',
        body: [
          '"Cancel the count" abandons it and writes nothing. Nothing you typed is applied.',
        ],
      },
    ],
  },

  transfers: {
    slug: 'transfers',
    title: 'Transfers',
    summary: 'Moving stock between branches.',
    sections: [
      {
        heading: 'Sending stock',
        steps: [
          'Create a transfer and choose where it is going.',
          'Add the items and quantities.',
          'Press "Ship — take the stock off the shelf".',
        ],
        body: [
          'Shipping takes the stock off your shelf immediately. It is in transit until the other branch receives it, so it belongs to neither shelf in between — which is exactly what you want if somebody counts while the van is out.',
        ],
      },
      {
        heading: 'Receiving stock',
        steps: [
          'Tick "Include transfers coming here" to see what is on its way.',
          'Open the transfer when it arrives.',
          'Press "Receive everything" if it all came, or type what actually arrived and press "Receive what was typed".',
        ],
        body: [
          'Receiving what actually arrived rather than what was sent is what makes a shortage visible. If you receive everything when something is missing, the loss disappears.',
        ],
      },
    ],
  },

  purchasing: {
    slug: 'purchasing',
    title: 'Purchasing',
    summary: 'Ordering from suppliers and booking in what arrives.',
    sections: [
      {
        heading: 'Raising an order',
        steps: [
          'Press Generate to have one built from what is below its reorder point, or start an empty one.',
          'Check the lines and quantities. While it says Draft, nothing has been ordered.',
          'Press "Post order". The stock is now expected, and shows as on order everywhere else.',
        ],
      },
      {
        heading: 'When it arrives',
        steps: [
          'Open the order and press "Record receipt".',
          'Type what actually came, which may not be everything.',
          'Save. Stock goes up by what you received, and the order shows as part received until the rest arrives.',
        ],
      },
      {
        heading: 'What the statuses mean',
        definitions: [
          { term: 'Draft', meaning: 'Being built. Nothing has been ordered and nothing is expected.' },
          { term: 'Posted', meaning: 'Sent to the supplier. The stock is expected.' },
          { term: 'Part received', meaning: 'Some of it has arrived. The rest is still expected.' },
          { term: 'Received', meaning: 'All of it arrived.' },
          { term: 'Cancelled', meaning: 'Called off. Only possible before anything is received.' },
        ],
      },
    ],
  },

  suppliers: {
    slug: 'suppliers',
    title: 'Suppliers',
    summary: 'Who you buy from.',
    sections: [
      {
        heading: 'Adding a supplier',
        steps: [
          'Press Add.',
          'Fill in at least the company name.',
          'Save. They can now be chosen on a purchase order.',
        ],
      },
      {
        heading: 'Deleting one',
        body: [
          'Deleting removes them from the list. Purchase orders already raised against them are not affected — the history stays.',
        ],
      },
    ],
  },

  receivables: {
    slug: 'receivables',
    title: 'Receivables',
    summary: 'Money customers owe you on account.',
    sections: [
      {
        heading: 'Taking a payment',
        steps: [
          'Find the customer and open their account.',
          'Type the amount and choose how they paid.',
          'Save. The balance comes down and the payment is on their statement.',
        ],
      },
      {
        heading: 'Voiding and refunding',
        definitions: [
          { term: 'Void', meaning: 'Cancels an invoice that should not have been raised. It stays on the ledger marked as void rather than disappearing.' },
          { term: 'Refund', meaning: 'Gives money back against an invoice. You are asked how much, and it cannot be more than the invoice is worth.' },
        ],
      },
      {
        heading: 'The aging report',
        body: [
          '"Aging report" groups what is owed by how late it is — current, one to thirty days, thirty-one to sixty, and sixty-one or more. The right-hand columns are the ones to worry about.',
        ],
      },
    ],
  },

  orders: {
    slug: 'orders',
    title: 'Orders and layaways',
    summary: 'Things promised to a customer but not yet taken away.',
    sections: [
      {
        heading: 'The three kinds',
        definitions: [
          { term: 'Order', meaning: 'Something a customer has asked you to get in for them.' },
          { term: 'Layaway', meaning: 'Goods set aside and paid for over time. The stock is held for them.' },
          { term: 'Quote', meaning: 'A price offered, with nothing committed on either side.' },
        ],
      },
      {
        heading: 'Filling an order',
        steps: [
          'Open the order.',
          'Press "Fill from stock" when the goods are available.',
          'The customer collects, and the order completes.',
        ],
      },
      {
        heading: 'Cancelling',
        body: [
          'Cancelling an order leaves any deposit on the customer’s account. Cancelling a layaway puts the goods back into stock and leaves the payments on the account. Neither takes money off anybody.',
        ],
      },
    ],
  },

  bulk: {
    slug: 'bulk',
    title: 'Batch changes',
    summary: 'Changing prices or tax flags on many items at once.',
    sections: [
      {
        heading: 'Before you press anything',
        body: [
          'This changes every item that matches the filters, all at once, and there is no undo. Take a backup first if the change is large.',
        ],
      },
      {
        heading: 'Repricing',
        steps: [
          'Choose which items with the filters at the top.',
          'Choose what to change and by how much.',
          'Look at the preview. It says exactly how many items match.',
          'Press the change button and confirm the count.',
        ],
      },
    ],
  },

  staff: {
    slug: 'staff',
    title: 'Staff',
    summary: 'Who works here, their hours and their commission.',
    sections: [
      {
        heading: 'Adding somebody',
        body: [
          'Colleagues are added on the Setup screen, under Users — that is where a sign-in and a staff record are created together, so the new person can both log in and be credited for a sale.',
        ],
      },
      {
        heading: 'Time clock',
        body: [
          'Clock in and clock out are on the header, on every screen. The hours recorded here are what the hours report reads.',
        ],
      },
    ],
  },

  settings: {
    slug: 'settings',
    title: 'Setup',
    summary: 'Everything the shop’s behaviour is read from.',
    sections: [
      {
        heading: 'How this screen works',
        body: [
          'Each tab is saved on its own. Changing something on one tab and moving to another without saving loses that change.',
        ],
      },
      {
        heading: 'The tabs worth knowing',
        definitions: [
          { term: 'Business ID', meaning: 'The shop’s name and address, as they appear on a receipt.' },
          { term: 'Taxes', meaning: 'The tax rates and when they take effect.' },
          { term: 'POS', meaning: 'How the till behaves — what it asks, what it does automatically.' },
          { term: 'Stations', meaning: 'One card per till, and the hardware each is wired to.' },
          { term: 'Tenders', meaning: 'The ways a customer can pay.' },
          { term: 'Users', meaning: 'Who can sign in, and what each of them is allowed to do.' },
        ],
      },
      {
        heading: 'Passwords',
        body: [
          'Passwords and PINs are stored hashed and can never be read back — not here and not anywhere. If somebody is locked out, set them a new password rather than looking up the old one.',
        ],
      },
    ],
  },

  admin: {
    slug: 'admin',
    title: 'Administration',
    summary: 'Setup, staff, backups, and the year-end close.',
    sections: [
      {
        heading: 'What is in here',
        body: [
          'The screens on this page change how the shop works rather than what it sells. Most of them are used rarely, and several cannot be undone — those say so before they do anything.',
        ],
      },
      {
        heading: 'The dangerous ones',
        definitions: [
          { term: 'Backup and restore', meaning: 'Restoring replaces the whole database. It asks you to type the file name.' },
          { term: 'Year end', meaning: 'Closing rolls a year up. Reopening discards that and asks you to type the year.' },
          { term: 'Bring data across', meaning: 'Writes an old system’s data into this one. The check and the dry run write nothing.' },
        ],
      },
    ],
  },

  backup: {
    slug: 'backup',
    title: 'Backup and restore',
    summary: 'A copy of the whole database in one file.',
    sections: [
      {
        heading: 'Taking a backup',
        steps: [
          'Press "Back up now".',
          'The file is written into the server’s backup folder and appears in the list.',
          'Copy it somewhere that is not this machine. A backup on the disk that fails with the database is not a backup.',
        ],
      },
      {
        heading: 'Restoring',
        body: [
          'Restoring replaces everything. Every sale, item and change made since that backup was taken is gone, and everybody signed in — including you — is thrown out while it runs. There is no undo, which is why you are asked to type the file name rather than just click.',
          'Take a fresh backup first if there is anything worth keeping since the one you are about to restore.',
        ],
      },
    ],
  },

  undelete: {
    slug: 'undelete',
    title: 'Undelete items',
    summary: 'Bringing back something that was deleted.',
    sections: [
      {
        heading: 'How it works',
        steps: [
          'Search for the name or code of what was deleted.',
          'Open it and restore it.',
          'It reappears wherever it was before, with its history.',
        ],
        body: [
          'Deleting in this system hides rather than destroys, which is what makes this screen possible.',
        ],
      },
    ],
  },

  audit: {
    slug: 'audit',
    title: 'Audit log',
    summary: 'Who changed what, and when.',
    sections: [
      {
        heading: 'What is recorded',
        body: [
          'Every change to a record, who made it and when. This is the screen to open when a figure is not what somebody expected — it says what happened to it rather than what it is now.',
        ],
      },
      {
        heading: 'Finding something',
        body: [
          'Narrow by date first, then by what kind of thing changed. The entries are in the order they happened.',
        ],
      },
    ],
  },

  rfid: {
    slug: 'rfid',
    title: 'RFID readers',
    summary: 'The tag readers, and whether they are working.',
    sections: [
      {
        heading: 'Is it working?',
        definitions: [
          { term: 'Connected', meaning: 'The reader is answering and tags will be picked up.' },
          { term: 'Disconnected', meaning: 'The reader is not answering. Check the cable and the power, then try again.' },
          { term: 'No tags found', meaning: 'The reader is working but there is nothing in range.' },
        ],
      },
      {
        heading: 'If a tag will not read',
        body: [
          'Check the reader shows as connected first — a disconnected reader and an unreadable tag look the same from the till. Metal and liquid near a tag both stop it being read.',
        ],
      },
    ],
  },

  accounting: {
    slug: 'accounting',
    title: 'Accounting',
    summary: 'Sending the shop’s figures to your accounts system.',
    sections: [
      {
        heading: 'What this does',
        body: [
          'It maps what happens in the shop onto the codes your accounts system expects, and sends the figures across. Nothing here changes a sale or a stock figure.',
        ],
      },
    ],
  },

  migration: {
    slug: 'migration',
    title: 'Bring data across',
    summary: 'Moving an old system’s records into this one.',
    sections: [
      {
        heading: 'The order to do it in',
        steps: [
          'Upload the file from the old system.',
          'Run the check. It writes nothing and tells you what it found.',
          'Run the dry run. It also writes nothing, and shows what would happen.',
          'Only then import. That writes to the catalogue and the stock ledger for real.',
        ],
      },
      {
        heading: 'If something looks wrong',
        body: [
          'Discard the staged rows and start again with a corrected file. Discarding affects only what has not been imported yet.',
        ],
      },
    ],
  },

  'year-end': {
    slug: 'year-end',
    title: 'Year end',
    summary: 'Closing a financial year.',
    sections: [
      {
        heading: 'Closing a year',
        body: [
          'Closing rolls the year up and writes a checkpoint. Nothing is deleted, and the sales themselves are untouched. It can be reopened afterwards if something was posted late.',
        ],
      },
      {
        heading: 'Reopening',
        body: [
          'Reopening discards the archive rows and checkpoints that closing produced. The sales they were derived from are not affected, and closing again rebuilds them. Because it throws away work, you are asked to type the year rather than click.',
        ],
      },
    ],
  },
};

/**
 * What to show for a topic nobody has written yet.
 *
 * Built from the registry rather than invented, so it is at least true: the screen's own name, what
 * it is for, and how to get to it.
 */
export function fallbackTopic(slug: string): HelpTopic | undefined {
  const route = ROUTES.find((r) => r.helpTopic === slug);

  if (!route) return undefined;

  return {
    slug,
    title: route.label,
    summary: `The ${route.label} screen.`,
    sections: [
      {
        heading: 'This guide is still being written',
        body: [
          `You can open this screen from ${route.section ? `the ${route.section} section of the menu` : 'the menu'}, or by pressing Ctrl+K and typing "${route.label}".`,
          'Until the full guide is here, ask a colleague or an administrator rather than guessing on a screen that changes stock or money.',
        ],
      },
    ],
  };
}

export function helpTopic(slug: string): HelpTopic | undefined {
  return HELP_TOPICS[slug] ?? fallbackTopic(slug);
}

/** Every topic that has a written guide, for the index. */
export const WRITTEN_TOPICS = Object.values(HELP_TOPICS);
