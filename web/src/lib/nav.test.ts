import { describe, expect, it } from 'vitest';
import { canOpen, commandsFor, groupPages, homeFor, navFor, navKeyForPath, P, pageForPath, pageForShortcut, PAGES, searchCommands, SUPPLY_CHAIN } from './nav';

const supervisor = ['web.signin', 'dashboard.view', 'reports.view', 'inventory.view', 'clients.view', 'giftcards.issue', 'timeclock.edit'];
const administrator = [
  'web.signin',
  'dashboard.view',
  'reports.view',
  'reports.staff',
  'inventory.view',
  'inventory.edit',
  'clients.view',
  'clients.edit',
  'suppliers.edit',
  'giftcards.issue',
  'setup.users',
  'setup.store',
  'stores.manage',
  'audit.view',
  'sync.send',
];

describe('the permission-to-navigation filter', () => {
  it('shows an administrator every page', () => {
    // The core pages; the grouped ones (Supply chain…) follow them, see "navigation groups" below.
    expect(navFor(administrator).filter((p) => !p.group).map((p) => p.title)).toEqual([
      'Dashboard',
      'Tills',
      'Sales Journal',
      'Inventory',
      'Clients',
      'Suppliers',
      'Stock',
      'Gift cards',
      'Reports',
      'Staff',
      'Packages',
      'Users',
      'Roles',
      'Stores',
      'Setup',
      'Audit log',
    ]);
  });

  it('hides what a role lacks', () => {
    const titles = navFor(supervisor).map((p) => p.title);
    expect(titles).toContain('Dashboard');
    expect(titles).toContain('Sales Journal');
    expect(titles).toContain('Tills');
    expect(titles).not.toContain('Staff');
    expect(titles).not.toContain('Users');
    expect(titles).not.toContain('Roles');
    expect(titles).not.toContain('Stores');
    expect(titles).not.toContain('Setup');
    expect(titles).not.toContain('Audit log');
  });

  it('shows nothing without web.signin', () => {
    expect(navFor(['dashboard.view', 'reports.view'])).toEqual([]);
    expect(canOpen({ permission: null }, ['reports.view'])).toBe(false);
  });

  it('leaves detail pages and Account out of the panel', () => {
    const keys = navFor(administrator).map((p) => p.key);
    expect(keys).not.toContain('sale');
    expect(keys).not.toContain('item');
    expect(keys).not.toContain('account');
  });

  it('takes a set as well as a list', () => {
    expect(navFor(new Set(supervisor)).length).toBe(navFor(supervisor).length);
  });

  it('lands on the dashboard, or the first page the role has', () => {
    expect(homeFor(administrator)).toBe('/');
    expect(homeFor(['web.signin', 'inventory.view'])).toBe('/tills/');
    expect(homeFor([])).toBe('/account/');
  });

  it('keeps each Ctrl key on its page whatever the role', () => {
    expect(pageForShortcut('Ctrl+4', administrator)?.title).toBe('Inventory');
    expect(pageForShortcut('Ctrl+4', ['web.signin', 'inventory.view'])?.title).toBe('Inventory');
    expect(pageForShortcut('Ctrl+0', supervisor)).toBeUndefined();
    const keys = PAGES.filter((p) => p.shortcut).map((p) => p.shortcut);
    expect(keys).toEqual(['Ctrl+1', 'Ctrl+2', 'Ctrl+3', 'Ctrl+4', 'Ctrl+5', 'Ctrl+6', 'Ctrl+7', 'Ctrl+8', 'Ctrl+9', 'Ctrl+0']);
  });

  it('finds the page of a path, with or without the trailing slash', () => {
    expect(pageForPath('/sales/view')?.key).toBe('sale');
    expect(pageForPath('/sales/view/')?.key).toBe('sale');
    expect(pageForPath('/')?.key).toBe('dashboard');
    expect(pageForPath('/nowhere/')).toBeUndefined();
    expect(navKeyForPath('/items/edit/')).toBe('items');
  });
});

describe('the Ctrl+K palette', () => {
  const reports = [
    { key: 'daily', title: 'Daily sales summary', group: 'Sales' },
    { key: 'stock', title: 'Inventory valuation', group: 'Inventory' },
  ];

  it('offers only what the user may open', () => {
    const titles = commandsFor(supervisor, reports).map((c) => c.title);
    expect(titles).toContain('Go to Dashboard');
    expect(titles).toContain('Daily sales summary');
    expect(titles).toContain('Sign out');
    expect(titles).not.toContain('Go to Users');
    expect(titles).not.toContain('Enrol a till');
    expect(titles).not.toContain('New item');
  });

  it('offers no reports without reports.view', () => {
    const titles = commandsFor(['web.signin', 'inventory.view'], reports).map((c) => c.title);
    expect(titles).not.toContain('Daily sales summary');
  });

  it('matches every typed word, titles that start with it first', () => {
    const commands = commandsFor(administrator, reports);
    expect(searchCommands(commands, 'inv')[0].title).toBe('Go to Inventory');
    expect(searchCommands(commands, 'sales sum').map((c) => c.title)).toEqual(['Daily sales summary']);
    expect(searchCommands(commands, 'customers').map((c) => c.title)).toContain('Go to Clients');
    expect(searchCommands(commands, 'zzz')).toEqual([]);
    expect(searchCommands(commands, '  ').length).toBe(commands.length);
  });
});

describe('the E4 pages', () => {
  const marketing = ['web.signin', 'dashboard.view', 'reports.view', 'clients.view', 'clients.edit', 'crm.view', 'crm.edit', 'marketing.campaigns', 'marketing.promotions'];
  const supervisorWithCrm = [...supervisor, 'crm.view'];

  it('shows CRM, my tasks, segments, campaigns, promotions and targets to the roles that have them', () => {
    const titles = navFor(marketing).map((p) => p.title);
    expect(titles).toEqual(expect.arrayContaining(['CRM', 'My tasks', 'Segments', 'Campaigns', 'Promotions']));
    expect(titles).not.toContain('Targets');
    expect(navFor([...marketing, 'sales.targets']).map((p) => p.title)).toContain('Targets');
    const supervisorTitles = navFor(supervisorWithCrm).map((p) => p.title);
    expect(supervisorTitles).toEqual(expect.arrayContaining(['CRM', 'My tasks']));
    expect(supervisorTitles).not.toContain('Campaigns');
    expect(supervisorTitles).not.toContain('Promotions');
    expect(navFor(administrator).map((p) => p.title)).not.toContain('CRM');
  });

  it('keeps the campaign page under Campaigns and out of the panel', () => {
    expect(pageForPath('/campaigns/view/')?.key).toBe('campaign');
    expect(navKeyForPath('/campaigns/view/')).toBe('campaigns');
    expect(navFor(marketing).map((p) => p.key)).not.toContain('campaign');
    expect(PAGES.filter((p) => ['crm', 'tasks', 'segments', 'campaigns', 'promotions', 'targets'].includes(p.key)).every((p) => !p.shortcut)).toBe(true);
  });

  it('offers the new things in Ctrl+K only with the right to make them', () => {
    const titles = commandsFor(marketing).map((c) => c.title);
    expect(titles).toEqual(expect.arrayContaining(['New lead', 'New opportunity', 'New activity', 'New segment', 'New campaign', 'New promotion']));
    expect(titles).not.toContain('New target');
    const viewer = commandsFor(supervisorWithCrm).map((c) => c.title);
    expect(viewer).toContain('Go to CRM');
    expect(viewer).not.toContain('New lead');
    expect(searchCommands(commandsFor(marketing), 'whatsapp')[0].title).toBe('Go to Campaigns');
    expect(searchCommands(commandsFor(marketing), 'coupon').map((c) => c.title)).toEqual(expect.arrayContaining(['Go to Promotions', 'New promotion']));
  });
});
describe('navigation groups (supply chain)', () => {
  const buyer = ['web.signin', 'inventory.view', P.purchasingManage, P.inventoryBatches];

  it('puts the supply chain pages under their heading after the core pages', () => {
    const everyKey = [...new Set(PAGES.map((p) => p.permission).filter((k): k is string => !!k)), 'web.signin'];
    const groups = groupPages(navFor(everyKey));
    // The core pages first (no heading), then each group in the order the list names them.
    expect(groups.map((g) => g.group)).toEqual([null, 'Finance', 'HR', 'Manufacturing', SUPPLY_CHAIN]);
    const supply = groups.find((g) => g.group === SUPPLY_CHAIN)!;
    expect(supply.pages.map((p) => p.title)).toEqual([
      'Purchase orders',
      'Requisitions',
      'Batches and expiry',
      'Bins',
      'Matching',
      'Landed costs',
      'Reorder suggestions',
      'Supplier performance',
    ]);
    expect(supply.pages.every((p) => !p.shortcut)).toBe(true);
  });

  it('shows each supply chain page only with its permission', () => {
    const titles = navFor(buyer).map((p) => p.title);
    expect(titles).toEqual(expect.arrayContaining(['Purchase orders', 'Requisitions', 'Matching', 'Landed costs', 'Reorder suggestions', 'Batches and expiry']));
    expect(titles).not.toContain('Bins');
    expect(titles).not.toContain('Supplier performance');
    expect(navFor([...buyer, P.inventoryBins, P.reports]).map((p) => p.title)).toEqual(expect.arrayContaining(['Bins', 'Supplier performance']));
  });

  it('finds the pages in the palette by what they hold, with their group', () => {
    const commands = commandsFor([...buyer, P.reports, P.inventoryBins]);
    const fefo = searchCommands(commands, 'expired');
    expect(fefo[0].title).toBe('Go to Batches and expiry');
    expect(fefo[0].group).toBe(SUPPLY_CHAIN);
    expect(searchCommands(commands, 'freight')[0].title).toBe('Go to Landed costs');
    expect(searchCommands(commands, 'supply chain').length).toBe(8);
    expect(commandsFor(buyer).find((c) => c.title === 'Go to Dashboard')).toBeUndefined();
  });
});
