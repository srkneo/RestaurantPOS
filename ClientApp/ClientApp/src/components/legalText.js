// DRAFT wording for Vyom Solutions to review (ideally with a lawyer) before the first paying restaurant.
// Words in [square brackets] still need to be filled in. English only for now.
export const LEGAL = {
  terms: {
    title: 'Terms of service',
    updated: '19 November 2026',
    sections: [
      ['Who we are', [
        'RestaurantPOS is provided by Vyom Solutions, India ("we", "us"). These terms apply to every business that uses the cloud version of RestaurantPOS ("you"). By starting a trial or paying for a plan you accept them.',
      ]],
      ['Free trial and plans', [
        'A new business gets a 30-day free trial with all features. No payment details are needed for the trial.',
        'After the trial the plans are ₹1,000 a month or ₹10,000 a year [plus GST, if applicable], paid in advance by UPI or bank transfer. We tell you at least 30 days before any price change.',
      ]],
      ['Late payment', [
        'For 7 days after your paid-up date the POS keeps working and shows a reminder. After that, new orders, bills and payments stop until the payment is received. Your data stays viewable and you can still export it.',
      ]],
      ['Your data', [
        'Your menu, orders, bills, staff and customer details belong to you. We use them only to run the service for you, as described in the privacy policy.',
        'You are responsible for the prices, tax rates, GSTIN, FSSAI number and other details printed on your bills, and for keeping your staff logins private.',
      ]],
      ['Fair use', [
        'Use RestaurantPOS only for your lawful business. Do not try to reach another business\'s data, overload or disrupt the service, or resell it without our written permission.',
      ]],
      ['Support access', [
        'When you ask for help, our support team may open your business in a time-limited support mode. Every such visit is recorded in your audit log.',
      ]],
      ['Availability and updates', [
        'We work to keep the service running at all times, take daily backups and release updates outside rush hours where possible. We cannot promise the service will never be interrupted (for example by internet, power or cloud provider failures), so keep a simple backup way to take orders.',
      ]],
      ['Stopping the service', [
        'You can stop using RestaurantPOS at any time. Payments already made are not refunded unless we agree otherwise.',
        'When an account closes, whether you close it or it stays unpaid for [30] days after it stopped, you have 30 days to export your data. After that we delete it, except what the law requires us to keep. GST law requires you to keep your own bills for several years, so export them before you leave.',
      ]],
      ['Limit of liability', [
        'As far as the law allows, we are not liable for indirect losses such as lost profit or lost business, and our total liability is limited to the fees you paid us in the 3 months before the claim.',
      ]],
      ['Changes to these terms', [
        'We will tell you in the app at least 15 days before these terms change. Continuing to use the service after that means you accept the new terms.',
      ]],
      ['Law and contact', [
        'These terms follow the laws of India, and the courts of [city], Gujarat decide any dispute. For questions, use the support details on the About page.',
      ]],
    ],
  },
  privacy: {
    title: 'Privacy policy',
    updated: '19 November 2026',
    sections: [
      ['What we collect', [
        'Business and owner details (name, email, mobile number), staff accounts (names, sign-in emails; passwords and PINs are stored only as one-way hashes), menus, orders, bills and payment records, customer names or phone numbers if your staff enter them, guest feedback, and technical logs (IP address, browser and error details).',
        'We never see card numbers: RestaurantPOS does not process card payments itself.',
      ]],
      ['Why we use it', [
        'To run the POS for you, keep sign-in secure, give support, manage your subscription and fix errors. We do not sell data and we do not show ads.',
      ]],
      ['Your customers\' data', [
        'For the details of your own customers (for example a phone number on a takeaway order), your business decides why they are collected and we process them only on your behalf, as the Digital Personal Data Protection Act, 2023 describes. Collect a customer\'s phone number only with their consent.',
      ]],
      ['Where it is stored', [
        'The cloud version runs on Amazon Web Services servers, currently in Sydney, Australia. All connections use HTTPS and we take daily backups.',
        'In the shop-PC edition your bills stay on your own PC; our cloud only checks the licence and sends updates.',
      ]],
      ['Who else handles it', [
        'Amazon Web Services (hosting and backups), Google Gemini (only the menu photos you upload, to read the dishes) and Telegram (our own service alerts to our team). Each handles the data only to provide that service to us.',
        'We share data with authorities only when the law requires it.',
      ]],
      ['How long we keep it', [
        'As long as your account is open. After an account closes you have 30 days to export, then we delete the data; older backup copies are replaced over time.',
      ]],
      ['Your rights', [
        'You can ask to see, correct or delete personal data, or raise a complaint. Write to our grievance officer: [name], [email]. We reply within [30] days.',
      ]],
      ['Cookies', [
        'We use only a sign-in cookie and a saved language choice. There are no tracking or advertising cookies.',
      ]],
      ['Children', [
        'RestaurantPOS accounts are for adults running a business, not for people under 18.',
      ]],
      ['Changes to this policy', [
        'We will tell you in the app before this policy changes in an important way.',
      ]],
    ],
  },
};
