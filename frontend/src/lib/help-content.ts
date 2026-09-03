import { ROUTES } from './routes';

/**
 * The guides, keyed by the topic a route resolves to.
 *
 * Written for somebody who is not technical and is probably in a hurry. Short numbered steps, the
 * words that are on the screen, and no jargon that the screen does not itself use.
 *
 * Deliberately incomplete. A topic with no entry here renders what the registry knows — what the
 * screen is called, what it is for, how to reach it — and says plainly that the full guide is still
 * being written. That is not a placeholder for its own sake: invented instructions for a screen
 * nobody verified are worse than none, because somebody will follow them confidently. Phase 4 of
 * the transformation fills the rest in from the screens themselves.
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
