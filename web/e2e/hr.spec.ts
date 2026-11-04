/**
 * HR and payroll (phase E3): every HR page against the mocked API, the HR group in the navigation and Ctrl+K, who may
 * open payroll, and a payroll run from draft to paid.
 */
import { expect, test } from '@playwright/test';
import { hrManager, hrOfficer, supervisor } from './fixtures';
import { mockApi } from './mock-api';

test('the HR group lists its pages under a heading, and Ctrl+K finds them by typing hr', async ({ page }) => {
  await mockApi(page);
  await page.goto('/hr/employees/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  await expect(nav.locator('[data-nav-group="HR"]')).toHaveCount(1);
  for (const name of ['Employees', 'Attendance', 'Shifts and rosters', 'Leave', 'Payroll', 'HR reports', 'HR settings']) {
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toBeVisible();
  }
  await expect(nav.getByRole('link', { name: /^Employees/ })).toHaveAttribute('aria-current', 'page');

  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Go to' });
  await page.keyboard.type('hr');
  await expect(palette.getByRole('option', { name: /Go to Payroll/ })).toContainText('HR · Screen');
  await expect(palette.getByRole('option', { name: /New payroll run/ })).toContainText('HR · Action');
  await expect(palette.getByRole('option', { name: /New leave request/ })).toBeVisible();
  await page.keyboard.press('Escape');
});

test('payroll is for those who run or approve it; a manager sees HR but not Payroll', async ({ page }) => {
  await mockApi(page, { user: hrManager });
  await page.goto('/hr/leave/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  await expect(nav.getByRole('link', { name: /^Leave/ })).toBeVisible();
  await expect(nav.getByRole('link', { name: /^Payroll/ })).toHaveCount(0);
  await page.goto('/hr/payroll/');
  await expect(page.getByText('Your role cannot open this page.')).toBeVisible();

  await mockApi(page, { user: supervisor });
  await page.goto('/');
  await expect(page.getByRole('navigation', { name: 'Pages' }).locator('[data-nav-group="HR"]')).toHaveCount(0);
});

test('employees are listed, opened and saved with their version', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/employees/');
  await expect(page.getByRole('heading', { name: 'Employees' })).toBeVisible();
  await expect(page.getByRole('row', { name: /E0001 Ayesha Khan/ })).toBeVisible();
  await page.locator('#emp-status').selectOption('all');
  await expect.poll(() => api.last('GET', '/api/hr/employees')?.query.get('status')).toBe('all');

  await page.getByRole('row', { name: /Ayesha Khan/ }).click();
  await expect(page).toHaveURL(/\/hr\/employees\/edit\/\?id=e-1$/);
  await expect(page.getByRole('heading', { name: 'Ayesha Khan' })).toBeVisible();
  await expect(page.getByText('Like 35202-1234567-1')).toBeVisible();
  await page.getByLabel('Phone', { exact: true }).fill('0300 7777777');
  await page.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByText('Ayesha Khan is saved.')).toBeVisible();
  const put = api.last('PUT', '/api/hr/employees/e-1');
  expect(put?.body).toMatchObject({ version: 'v-1', phone: '0300 7777777', cnic: '35202-1234567-1', paymentMethod: 'Bank' });

  await page.getByRole('tab', { name: 'Pay and statutory' }).click();
  await expect(page.getByText('Like PK36SCBL0000001123456702')).toBeVisible();
  await page.getByRole('tab', { name: 'Salary' }).click();
  await expect(page.getByRole('row', { name: /Yearly|60,000/ }).first()).toBeVisible();
  await page.getByRole('button', { name: /New salary/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New salary' });
  await dialog.getByLabel('Basic a month').fill('65000');
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => (api.last('POST', '/api/hr/employees/e-1/salary')?.body as { basic?: number })?.basic).toBe(65000);

  await page.getByRole('tab', { name: 'Documents' }).click();
  await expect(page.getByRole('row', { name: /CNIC copy/ })).toBeVisible();
  await page.getByRole('tab', { name: 'Leave' }).click();
  await expect(page.getByRole('row', { name: /Annual leave/ }).first()).toBeVisible();
});

test('a new employee gets the next code, and an old version is a conflict', async ({ page }) => {
  const api = await mockApi(page, {
    overrides: { 'PUT /api/hr/employees/e-2': () => ({ status: 409, json: { error: 'Someone else changed this record.' } }) },
  });
  await page.goto('/hr/employees/edit/');
  await expect(page.getByRole('heading', { name: 'New employee' })).toBeVisible();
  await page.getByLabel('Full name').fill('Zara Iqbal');
  await page.getByRole('tab', { name: 'Job' }).click();
  await expect(page.getByLabel('Employee code')).toHaveValue('E0004');
  await page.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => (api.last('POST', '/api/hr/employees')?.body as { fullName?: string })?.fullName).toBe('Zara Iqbal');
  await expect(page).toHaveURL(/\?id=e-new$/);

  await page.goto('/hr/employees/edit/?id=e-2');
  await expect(page.getByRole('heading', { name: 'Bilal Ahmed' })).toBeVisible();
  await page.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByRole('dialog', { name: /Someone else changed this employee/ })).toBeVisible();
});

test('attendance by day, the monthly sheet and corrections', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/attendance/');
  await expect(page.getByRole('heading', { name: 'Attendance' })).toBeVisible();
  const late = page.getByRole('row', { name: /Ayesha Khan.*Present/ });
  await expect(late).toContainText('25 min');
  await expect(page.getByRole('row', { name: /Bilal Ahmed.*Absent/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Corrected/ })).toContainText('1 h 05 min');

  await page.getByRole('tab', { name: 'Monthly sheet' }).click();
  const sheet = page.getByRole('table', { name: 'Attendance sheet' });
  await expect(sheet.getByRole('row', { name: /E0001 Ayesha Khan/ })).toContainText('CL');
  expect(api.last('GET', '/api/hr/attendance/sheet')?.query.get('month')).toMatch(/^\d{4}-\d{2}$/);

  await page.getByRole('tab', { name: 'Corrections' }).click();
  const row = page.getByRole('row', { name: /At the supplier all day/ });
  await row.getByRole('button', { name: /Approve/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: /^Approve/ }).click();
  await expect(page.getByText('The correction is approved.')).toBeVisible();
  expect(api.last('POST', '/api/hr/attendance/corrections/corr-1/approve')).toBeTruthy();

  await page.getByRole('button', { name: /Ask for a correction/ }).click();
  const ask = page.getByRole('dialog', { name: 'Ask for a correction' });
  await ask.locator('#corr-employee').selectOption('e-2');
  await ask.getByLabel('Reason').fill('Forgot to punch out');
  await ask.getByLabel('Minutes worked').fill('480');
  await ask.getByRole('button', { name: /^Ask/ }).click();
  await expect.poll(() => api.last('POST', '/api/hr/attendance/corrections')?.body).toMatchObject({ employeeId: 'e-2', workedMinutes: 480, reason: 'Forgot to punch out' });
});

test('shifts with their days, and the week roster', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/shifts/');
  await expect(page.getByRole('row', { name: /Morning.*09:00–17:00.*Mon–Sat/ })).toBeVisible();
  await page.getByRole('button', { name: /New shift/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New shift' });
  await dialog.getByLabel('Name').fill('Night');
  await dialog.getByLabel('Starts').fill('22:00');
  await dialog.getByLabel('Ends').fill('06:00');
  await dialog.getByLabel('Sun').check();
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/hr/shifts')?.body).toMatchObject({ name: 'Night', startTime: '22:00', endTime: '06:00', days: expect.arrayContaining(['Sunday', 'Monday']) });

  await page.getByRole('tab', { name: 'Roster' }).click();
  await expect(page.getByRole('row', { name: /Ayesha Khan/ })).toContainText('Day off');
  await page.getByLabel('Shift of Ayesha Khan this week').selectOption('sh-2');
  await page.getByRole('button', { name: /Save the week/ }).click();
  await expect(page.getByText('The roster is saved.')).toBeVisible();
  const put = api.last('PUT', '/api/hr/roster')?.body as { weekStart: string; assignments: unknown[] };
  expect(put.assignments).toEqual([{ employeeId: 'e-1', shiftId: 'sh-2' }]);
  expect(new Date(put.weekStart + 'T00:00:00').getDay()).toBe(1);
});

test('leave is asked for, approved and its balances shown', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/leave/?new=1');
  const ask = page.getByRole('dialog', { name: 'New leave request' });
  await expect(ask).toBeVisible();
  await ask.locator('#req-employee').selectOption('e-1');
  await ask.locator('#req-type').selectOption('lt-cl');
  await ask.getByLabel('Half a day').check();
  await ask.getByRole('button', { name: /^Ask/ }).click();
  await expect.poll(() => api.last('POST', '/api/hr/leave-requests')?.body).toMatchObject({ employeeId: 'e-1', leaveTypeId: 'lt-cl', firstDayHalf: true });

  const pending = page.getByRole('row', { name: /Family wedding/ });
  await pending.getByRole('button', { name: /Approve/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: /^Approve/ }).click();
  await expect(page.getByText('The leave is approved.')).toBeVisible();

  await page.getByRole('tab', { name: 'Approvals' }).click();
  await expect.poll(() => api.last('GET', '/api/hr/leave-requests')?.query.get('status')).toBe('Pending');
  await page.getByRole('tab', { name: 'Balances' }).click();
  await expect(page.getByRole('row', { name: /Unpaid leave/ })).toContainText('No limit');
  await expect(page.getByRole('row', { name: /Annual leave/ })).toContainText('7.67');
});

test('a payroll run goes from draft to paid, each step sending its version', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/payroll/');
  await expect(page.getByRole('heading', { name: 'Payroll' })).toBeVisible();
  await page.getByRole('row', { name: /#10000000001/ }).click();
  await expect(page).toHaveURL(/\/hr\/payroll\/run\/\?id=run-1$/);
  await expect(page.getByText('Not calculated yet')).toBeVisible();

  await page.getByRole('button', { name: /^Calculate/ }).click();
  await expect(page.getByText(/Calculated: 2 payslips/)).toBeVisible();
  expect(api.last('POST', '/api/payroll/runs/run-1/calculate')?.body).toEqual({ version: 'v-Draft' });
  await expect(page.getByRole('row', { name: /Ayesha Khan/ })).toContainText('$73,645.50');
  await expect(page.getByText('E0003 Sana Malik: no salary on 1 Nov 2026.')).toBeVisible();

  await page.getByRole('button', { name: /^Approve/ }).click();
  await expect(page.getByText('The run is approved.')).toBeVisible();
  await page.getByRole('button', { name: /Post to the ledger/ }).click();
  await expect(page.getByText(/Posted to the ledger as entry #10000000042/)).toBeVisible();
  await page.getByRole('button', { name: /Record payment/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: /Record payment/ }).click();
  await expect(page.getByText(/Paid, entry #10000000043/)).toBeVisible();
  expect(api.last('POST', '/api/payroll/runs/run-1/pay')?.body).toMatchObject({ version: 'v-Posted', bankAccount: null, cashAccount: null });
  await expect(page.getByRole('list', { name: 'Steps' }).getByText('5. Paid')).toHaveAttribute('aria-current', 'step');

  await page.getByRole('tab', { name: 'Cash sheet' }).click();
  await expect(page.getByRole('row', { name: /Bilal Ahmed/ })).toContainText('$74,600.00');
  await page.getByRole('tab', { name: 'Salary sheet' }).click();
  await page.getByRole('button', { name: /Bank transfer list/ }).click();
  await expect.poll(() => api.last('GET', '/api/payroll/runs/run-1/bank-transfers')).toBeTruthy();
});

test('an HR officer prepares payroll but does not approve it', async ({ page }) => {
  await mockApi(page, { user: hrOfficer });
  await page.goto('/hr/payroll/run/?id=run-2');
  await expect(page.getByRole('heading', { name: /Final settlement of Sana Malik/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /Calculate again/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Approve/ })).toHaveCount(0);
});

test('a stale run is a conflict that offers to reload', async ({ page }) => {
  await mockApi(page, {
    overrides: { 'POST /api/payroll/runs/run-1/calculate': () => ({ status: 409, json: { error: 'Someone else changed this record.' } }) },
  });
  await page.goto('/hr/payroll/run/?id=run-1');
  await page.getByRole('button', { name: /^Calculate/ }).click();
  await expect(page.getByRole('dialog', { name: /Someone else changed this payroll run/ })).toBeVisible();
});

test('payslips print with their sections and the tax projection', async ({ page }) => {
  await mockApi(page);
  await page.goto('/hr/payroll/payslip/?id=ps-1');
  const slip = page.getByRole('article', { name: 'Payslip' });
  await expect(slip.getByRole('heading', { name: /Payslip/ })).toBeVisible();
  await expect(slip.getByText('Earnings')).toBeVisible();
  await expect(slip.getByText('Paid by the employer')).toBeVisible();
  await expect(page.getByTestId('net-pay')).toHaveText('$73,645.50');
  await expect(slip.getByText("The year's taxable income, projected")).toBeVisible();
});

test('loans are listed and paid', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/payroll/');
  await page.getByRole('tab', { name: 'Loans and advances' }).click();
  await expect(page.getByRole('row', { name: /School fees|Ayesha Khan/ }).first()).toContainText('$20,000.00');
  await page.getByRole('button', { name: /New loan/ }).click();
  const dialog = page.getByRole('dialog', { name: /New loan/ });
  await dialog.locator('#loan-employee').selectOption('e-2');
  await dialog.getByLabel('Amount').fill('12000');
  await dialog.getByLabel('Taken back a month').fill('3000');
  await dialog.locator('#loan-from').selectOption('Bank');
  await dialog.getByRole('button', { name: /Pay it/ }).click();
  await expect.poll(() => api.last('POST', '/api/payroll/loans')?.body).toMatchObject({ employeeId: 'e-2', amount: 12000, instalment: 3000, paidFrom: 'Bank' });
});

test('HR reports run with their parameters and download as CSV', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/hr/reports/');
  await page.getByRole('navigation', { name: 'HR reports' }).getByRole('button', { name: 'EOBI return' }).click();
  await page.getByRole('button', { name: /Run report/ }).click();
  const table = page.getByRole('table', { name: 'EOBI return' });
  await expect(table.getByRole('row', { name: /Ayesha Khan/ })).toBeVisible();
  await expect(table.locator('tfoot')).toContainText('$4,000.00');
  expect(api.last('GET', '/api/hr/reports/eobi')?.query.get('month')).toMatch(/^\d{4}-\d{2}$/);
  await page.getByRole('button', { name: /Download CSV/ }).click();
  await expect.poll(() => api.last('GET', '/api/hr/reports/eobi/csv')).toBeTruthy();

  await page.getByRole('navigation', { name: 'HR reports' }).getByRole('button', { name: 'Salary sheet' }).click();
  await expect(page.getByRole('button', { name: /Run report/ })).toBeDisabled();
});

test('HR settings: shared lists, leave types, holidays, the rule sets with their budget note, and stores', async ({ page }) => {
  await mockApi(page);
  await page.goto('/hr/settings/');
  await expect(page.getByRole('row', { name: /Sales floor/ })).toBeVisible();
  await page.getByRole('tab', { name: 'Designations' }).click();
  await expect(page.getByRole('row', { name: /Store manager/ })).toBeVisible();
  await page.getByRole('tab', { name: 'Leave types' }).click();
  await expect(page.getByRole('row', { name: /Unpaid leave/ })).toContainText('No limit');
  await page.getByRole('tab', { name: 'Holidays' }).click();
  await expect(page.getByRole('row', { name: /Iqbal Day/ })).toBeVisible();
  await page.getByRole('tab', { name: 'Payroll rule sets' }).click();
  await expect(page.getByTestId('budget-note')).toContainText('change with every budget');
  await expect(page.getByText('Tax year 2026 only (1 July 2025 to 30 June 2026). Check every figure after each budget (June).')).toBeVisible();
  await expect(page.getByRole('cell', { name: '35% of the excess' })).toBeVisible();
  await page.getByRole('button', { name: /Start the next tax year/ }).click();
  const dialog = page.getByRole('dialog', { name: 'The next tax year' });
  await expect(dialog.getByLabel('From', { exact: true })).toHaveValue('2026-07-01');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await page.getByRole('tab', { name: 'Stores' }).click();
  await expect(page.getByRole('row', { name: /WEST/ })).toContainText('Its own');
});

test('a store administrator reads the shared HR lists but does not change them', async ({ page }) => {
  await mockApi(page, { user: hrOfficer });
  await page.goto('/hr/settings/');
  await expect(page.getByRole('row', { name: /Sales floor/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /New department/ })).toHaveCount(0);
  await expect(page.getByText('These lists are shared by every store; a super administrator changes them.')).toBeVisible();
});
