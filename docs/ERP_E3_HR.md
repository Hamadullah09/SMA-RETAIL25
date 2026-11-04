# E3: HR and payroll

Phase E3 of the [ERP plan](ERP_PLAN.md), on branch `erp-e3-hr`, 2026-11-04. It adds employees, shifts and attendance,
leave, and payroll to the server and the web back office. Payroll posts to the general ledger of section 6 of the plan.

## 1. What is there

| Area | What it does | Code |
|---|---|---|
| Country rules | A payroll rule set per country and period (data, not code): income tax slabs, surcharge, minimum wage, EOBI and social security, the working week, overtime, absence and lateness, gratuity, how allowances are taxed, CNIC and IBAN formats. Leave types and public holidays per country. Pakistan is seeded on the server's first start. Each store uses its own country (HR settings › Stores) or the company's (the ledger settings' country, PK by default) | `Core/Model/Hr.cs`, `HrService` |
| Employees | Identity (CNIC), contact, emergency contact, joining, confirmation and leaving dates, type, HR department, designation, store (none for head office), manager, user at the tills, usual shift, bank and statutory numbers, income from a previous employer, documents (up to 10 MB each). The CNIC and IBAN are checked against the rule set's formats and the IBAN check digits | `HrService` |
| Shifts and attendance | Shifts with break, grace minutes and days of the week; a roster per week with changes per day. Attendance is worked out from the time clock (`TimeClockEntry`) when asked, never stored: present, late, left early, absent, on leave, holiday, day off, overtime. A correction counts once someone other than the one who asked approves it | `AttendanceRules`, `AttendanceService`, `HrCalendar` |
| Leave | Requests with half days, counted in working days (or calendar days for maternity). The employee's manager or someone with `hr.leave` decides, never the employee. Balances per calendar year: earned a twelfth a month where the type says so, carried over up to a limit, adjusted by hand | `LeaveService` |
| Salaries and loans | A salary is a basic pay with allowances and fixed deductions from a date; a change is a new row. Loans and salary advances are paid through the ledger and taken back in monthly instalments | `PayrollService` |
| Payroll runs | A month for one store or the whole company, or a final settlement for one leaver: Draft → Calculated → Approved (by someone with `payroll.approve`) → Posted → Paid. The bank transfer list (CSV), the cash sheet and printable payslips | `PayrollService`, `PayrollRules` |
| Reports | Salary sheet, tax deducted (certificate), EOBI return, social security return, attendance, leave taken, overtime, headcount and turnover; as tables or CSV | `Endpoints.Payroll.cs` |
| Web | An HR group at the end of the navigation: Employees, Attendance, Shifts and rosters, Leave, Payroll, HR reports, HR settings. Ctrl+K finds them by typing "hr" | `web/src/app/(app)/hr/` |

### How a payslip is worked out

1. **Days.** The days employed in the month (joiners and leavers are paid for their days). Pay for part of a month is
   divided by the rule set's basis: the days of the month (seeded), 30, or the working days.
2. **Earnings.** Basic pay and allowances for those days; a daily-wage employee is paid for the days worked and paid
   leave. Overtime at the hourly rate (the month's pay ÷ the day basis ÷ hours a day) times the multiplier. Commission
   from the commission rules, for the employee's user. A settlement adds leave paid out and gratuity.
3. **Time not worked.** Unpaid leave and days absent at the day's rate; lateness by the rule set (a day for every three
   late arrivals, seeded). Never more than the pay.
4. **Income tax.** The annualised projection: the tax year's taxable income is what was paid before (earlier payslips
   and a previous employer's figures), this month, and the regular monthly pay for each month still to come; the tax on
   that, less tax already deducted, is spread over this month and the months to come. A settlement projects nothing.
   Medical allowance is free of tax up to 10% of basic pay; other allowances are taxed.
5. **Deductions.** Tax, the employee's EOBI, fixed deductions and loan instalments. Net pay is never below zero:
   instalments wait first, then fixed deductions.
6. **Employer's part.** EOBI and social security, paid on top.

## 2. Pakistan's rule set and its tax year

The seeded rule set is **"Pakistan, tax year 2026 (July 2025 to June 2026)"**, in effect from **1 July 2025 to 30 June
2026**. It ends with its tax year on purpose: the Finance Act 2026 changed the slabs from 1 July 2026 (section 2.3), so a
month after June 2026 is not taxed by the old slabs. Payroll for such a month pays nobody and says on the run: "there
are no payroll rules for PK on …; add that tax year's rule set in HR settings". *HR settings › Payroll rule sets › Start
the next tax year* copies the last rule set a year on, without an end, with a note asking for every figure to be checked.

### 2.1 Figures that were checked

Checked on 2026-11-04 against published summaries by payroll and tax advisers (named below), **not** against the
Gazette of Pakistan or the notifications themselves.

| Figure | Seeded | Source |
|---|---|---|
| Salaried tax slabs, tax year 2026 | 0 to 600,000: none. 600,000 to 1,200,000: 1% above 600,000. 1,200,000 to 2,200,000: 6,000 + 11% above 1,200,000. 2,200,000 to 3,200,000: 116,000 + 23%. 3,200,000 to 4,100,000: 346,000 + 30%. Above 4,100,000: 616,000 + 35% | Finance Act 2025, as summarised by Vialto Partners and Mercans (July 2025). Each slab's fixed tax is the tax at its start by the slab before (tested) |
| Surcharge (section 4AB) | 9% of the tax above Rs 10 million taxable income | Same sources (reduced from 10% from 1 July 2025) |
| Minimum wage | Rs 40,000 a month | Punjab, Sindh and Khyber Pakhtunkhwa from 1 July 2025 (Vialto Partners, October 2025; Dawn on Punjab's notification). Balochistan stayed at Rs 37,000 |
| EOBI | 5% of the minimum wage from the employer (Rs 2,000), 1% from the employee (Rs 400) | Vialto Partners, October 2025, and others. Note: Dawn reports court rulings in Sindh and Balochistan that the contribution is 6% of the minimum wage |
| Social security (PESSI, Punjab) | 6% of the minimum wage (Rs 2,400), by the employer | Vialto Partners: applied on the minimum wage whatever the wage paid. This phase changed the seeded basis from the wage earned to the minimum wage |
| Medical allowance | Free of tax up to 10% of basic pay | Clause 139, Part I, Second Schedule (PwC tax summaries), where medical expenses are not reimbursed |

### 2.2 Figures that could not be checked

These are seeded as they were, and must be checked by HR or a tax adviser before relying on them:

- **Social security coverage limit** of Rs 40,000 (employees earning more are not covered). News reports say Punjab
  notified a wage ceiling equal to the minimum wage and that the Punjab Employees Social Security (Amendment) Act 2026
  changed the old threshold; the figure now in force was not found.
- **The working week** (6 days, 48 hours), **overtime at double** the hourly rate from 30 minutes, **gratuity** of 30
  days' pay a year after 12 months (a part year over 6 months counting as a year), and the **leave entitlements** (14
  days annual leave after 12 months, 10 casual, 8 sick, 84 days maternity after 4 months, 7 days paternity). Provinces
  have changed maternity leave since the 1958 ordinance (Sindh by an act of 2018); check the province's law.
- **Lateness** (a day's pay for every three late arrivals) and **paternity leave** are company policy, not law.
- **The 2026 holidays.** The Islamic holidays follow the moon; their seeded dates are the expected ones and must be
  checked against the Cabinet Division's notification. Holidays of 2027 are not seeded.
- The **CNIC and IBAN formats** are the standard ones (00000-0000000-0; PK, 2 check digits, 4 letters, 16 digits).

### 2.3 Tax year 2027 (from 1 July 2026): not seeded

The Finance Act 2026 was enacted on 26 June 2026 (Orbitax). Two sources give the same salaried table for tax year
2027: Vialto Partners' summary of the Finance Bill (June 2026, which says the bill became the Act) and the Pakistan
Observer (1 July 2026). It is internally consistent, but it was **not** checked against the Act, so it is given here
only as a starting point for HR to check and enter:

| Taxable income a year | Tax (as reported) |
|---|---|
| Up to 600,000 | None |
| 600,000 to 1,200,000 | 1% above 600,000 |
| 1,200,000 to 2,200,000 | 6,000 + 11% above 1,200,000 |
| 2,200,000 to 3,200,000 | 116,000 + 20% above 2,200,000 |
| 3,200,000 to 4,100,000 | 316,000 + 25% above 3,200,000 |
| 4,100,000 to 5,600,000 | 541,000 + 29% above 4,100,000 |
| 5,600,000 to 7,000,000 | 976,000 + 32% above 5,600,000 |
| Above 7,000,000 | 1,424,000 + 35% above 7,000,000 |

The 9% surcharge on salaried income above Rs 10 million is reported withdrawn. The 2026-27 minimum wages are reported
differently by different sources (federal Rs 40,700; Sindh Rs 43,000; Punjab unconfirmed), and nothing was found on
EOBI for 2026-27.

### 2.4 Payslips checked by hand

The data tests (`PayrollTests`) calculate July 2025, the first month of tax year 2026, so a month's tax is a twelfth
of the year's:

| Basic a month | Taxable a year | Tax a year | Tax a month | Net pay |
|---|---|---|---|---|
| 40,000 | 480,000 | 0 | 0 | 39,600 |
| 80,000 | 960,000 | 3,600 | 300 | 79,300 |
| 150,000 | 1,800,000 | 72,000 | 6,000 | 143,600 |
| 250,000 | 3,000,000 | 300,000 | 25,000 | 224,600 |
| 300,000 | 3,600,000 | 466,000 | 38,833.33 | 260,766.67 |
| 500,000 | 6,000,000 | 1,281,000 | 106,750 | 392,850 |
| 1,000,000 | 12,000,000 | 3,381,000 + 9% = 3,685,290 | 307,107.50 | 692,492.50 |

Net pay is less EOBI of Rs 400. Each employee also costs the employer EOBI of Rs 2,000, and the one earning Rs 40,000
social security of Rs 2,400. The tests also check allowances (house rent taxed, medical free up to 10% of basic), a
joiner, unpaid leave, absence and overtime, a raise in August (the projection then gives 13,136.36 a month), income from
a previous employer, and a final settlement with leave paid out and seven years' gratuity.

### 2.5 Not modelled

- Gratuity is taxed in full; the Second Schedule's exemption for gratuity is not applied.
- Tax credits and reductions (donations, Zakat, pension funds, teachers' rebate), tax on arrears spread over earlier
  years, and the employer's annual and monthly withholding statements (section 165) are not worked out.
- A rule set is per country, not per province: stores in Punjab and Balochistan (minimum wage Rs 37,000) cannot have
  different EOBI or social security figures. Turn social security off on the employees it does not apply to.
- Provident fund and a gratuity fund are not modelled; a fixed deduction with a ledger account can stand in for a fund.

## 3. Posting to the general ledger

Payroll posts through `LedgerService` (source `payroll:{run id}`, dated the period's last day), which refuses an entry
that does not balance. For a run:

| Debit | Credit |
|---|---|
| 6010 Salaries and wages: earnings less time not worked, per store | 2300 Salaries payable: each employee's net pay (with a fixed deduction that has no account of its own), as a line with the employee as party |
| 6020 Employer contributions: EOBI and social security, per store | 2310 Income tax withheld |
| | 2320 Social security and pension contributions payable: the employees' EOBI and the employer's part |
| | 1150 Advances to employees: loan instalments, with the employee as party |
| | The account of a fixed deduction that names one (a canteen, a fund) |

Paying a run (`payroll-payment:{run id}`) debits 2300 for each net pay and credits the bank (1050) for transfers and the
cash (1010) for cash, or the accounts given. A loan is paid with Dr 1150 and Cr the cash or bank it was paid from, and a
cancelled loan is reversed. The tests post a run with a bank-paid, a cash-paid, a canteen and a fund deduction and a loan
instalment, and check every account, that both entries balance, and that salaries payable keeps only the fund.

## 4. Sync placement

| What | Where it is kept |
|---|---|
| Every HR and payroll table (rule sets, leave types, holidays, departments, designations, employees and documents, shifts, roster, corrections, leave, salaries, loans, runs, payslips) | The server only (`ILocalOnly`): never in a till's package |
| The audit lines about them, and about ledger entries | The server only (`SyncPolicy`, the line after the loyalty transactions). Ledger lines were added in this phase, because payroll names employees with their pay and loans in its entries |
| Users, with their staff code and PIN | As before: down to their store's tills, so employees punch in at the till |
| Time clock entries | As before: made at the till and sent up; attendance is worked out from them on the server |

A server test checks that a joined till gets the employee's user and none of the employee, salary, loan, ledger entry
or audit lines about them.

## 5. Permissions, roles and the audit log

| Key | Allows |
|---|---|
| `hr.view` | See employees, shifts, attendance and leave; ask for leave |
| `hr.edit` | Add and change employees, HR lists, shifts, leave types, holidays and payroll rules |
| `hr.attendance` | Rosters, attendance corrections and their approval (by someone else) |
| `hr.leave` | Approve leave for anyone but oneself, adjust balances |
| `payroll.run` | Salaries, loans, payroll runs, posting and paying them |
| `payroll.approve` | Approve and reopen payroll runs |

Salaries, loans, runs, payslips and the payroll reports are read with `payroll.run` or `payroll.approve` only. The
standard **HR officer** role has every HR key and `payroll.run`, but not `payroll.approve`, so a second person approves.
The Manager role has `hr.view` and `hr.leave`. Administrators of one store see that store's employees; head office
employees, company-wide runs and the shared HR lists (when there is more than one store) are for users who see every
store.

Employees, HR lists, shifts, leave types, holidays and rule sets are audited tables; the CNIC, IBAN, NTN, EOBI and social
security numbers and the previous employer's figures are written as "(hidden)". Salary changes, loans, leave decisions,
corrections, documents and every step of a payroll run have their own audit lines, which say what happened, not the
amounts.

## 6. Endpoints

`/api/hr/…` and `/api/payroll/…`, described in [API.md](API.md), section "HR and payroll (phase E3)".

## 7. Tests

| Where | What |
|---|---|
| `tests/RetailPlus.Core.Tests/HrRulesTests.cs` | Tax by slabs and surcharge, the projection, tax years, service and gratuity, CNIC and IBAN checks, lateness, overtime, night shifts, leave days |
| `tests/RetailPlus.Data.Tests/HrTests.cs` | On SQLite and SQL Server: the seeded rule set, rule sets as data, employees and the hidden audit values, lists and documents, attendance with holidays, roster and corrections, leave with approvals and balances |
| `tests/RetailPlus.Data.Tests/PayrollTests.cs` | On SQLite and SQL Server: the payslips of section 2.4, joiners, leave, absence and overtime, the projection, loans, the steps and who may take them, a month without rules, the final settlement, the ledger entries |
| `tests/RetailPlus.Server.Tests/HrApiTests.cs` | Permissions, store limits, versions, a run from draft to paid through the API, leave and corrections, what reaches a till |
| `web/src/lib/hr.test.ts`, `hr-api.test.ts` | The HR client, words, run buttons, payslip sections, navigation, starting the next tax year |
| `web/e2e/hr.spec.ts`, `hr-layout.spec.ts` | Every HR page against the mocked API, payroll from draft to paid, starting the next tax year; no sideways scroll on desktop or a phone |

## 8. Still to do

- Enter the rule set of tax year 2027 after checking it against the Finance Act 2026 and the 2026-27 notifications
  (section 2.3), and the holidays of 2027.
- Have a tax adviser check the figures of section 2.2, and decide on the items of section 2.5.
- Nothing for employees themselves: no "my payslips" or "ask for leave" at the till or on the web for an employee who
  is not an HR user; payslips are printed, not sent.
- Bank files are a plain CSV; no bank's own upload format.
- Anyone with `hr.view` sees an employee's CNIC and IBAN. A stricter rule (only `hr.edit` and payroll) would need the
  API and the employee page changed together.
