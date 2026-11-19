// Short how-to pages for restaurant staff, in English and Gujarati. Plain steps, no screenshots.
export const HELP = {
  en: [
    { id: 'order', title: 'Take an order', steps: [
      'Open POS. Choose Dine-in and the table, or Takeaway and the customer name.',
      'Tap items to add them. Items with sizes or extras ask first. Tap + / - to change the quantity, or add a note like "Jain".',
      'Press Place order. The kitchen screen shows it at once; press Print KOT if you use paper tickets.',
    ] },
    { id: 'tables', title: 'Tables and running tabs', steps: [
      'A table with an open order shows its token in the table list. Choosing it adds your items to that order as the next round.',
      'Only the new round goes to the kitchen and on the KOT.',
      'In Orders: Move table moves guests to a free table, Merge table joins two tables into one bill, Split bill moves some items to a bill of their own.',
    ] },
    { id: 'kitchen', title: 'Kitchen screen and stations', steps: [
      'Open Kitchen on the kitchen PC or tablet. New orders appear by themselves; tap Start cooking, then Mark ready.',
      'With stations (like Tandoor or Bar) set on the Menu page, each screen can show only its station, and one KOT prints per station.',
      'The Token screen on a TV shows tokens being prepared and ready to collect.',
    ] },
    { id: 'bills', title: 'Bills and payments', steps: [
      'Press Bill on the order. Add a discount if needed and make the bill.',
      'Take Cash, UPI (the QR shows the exact amount) or Card. A bill can be paid in parts.',
      'Add tip records a tip for the waiter; it is not part of the bill or GST.',
      'A printed bill can be voided only with a reason and a manager PIN.',
    ] },
    { id: 'qr', title: 'QR ordering at the table', steps: [
      'Tables > QR codes: print the sheet and put each code on its table.',
      'Turn on "Guests can order from the QR code" to let guests order; their orders go straight to the kitchen on that table\'s tab.',
      'When a guest presses Call waiter or Ask for bill, the POS shows a message.',
      'If a code is misused, press New code for that table and print it again.',
    ] },
    { id: 'dayclose', title: 'Day close', steps: [
      'Open the shift in the morning with the cash in the drawer.',
      'Record money spent on the Expenses page; "from the till" lowers the cash expected.',
      'At night, count the cash and close the shift. The Day report (Z report) shows sales, GST, payments, tips and expenses; Share on WhatsApp sends a summary.',
    ] },
    { id: 'reports', title: 'Reports and GST', steps: [
      'Reports shows sales for any days: by day, category, item, payment, hour and staff.',
      'Download GST bills (CSV) gives your accountant every bill with taxable value, CGST and SGST.',
    ] },
    { id: 'shop', title: 'Shop PC: backups and updates', steps: [
      'Keep the USB drive named POSBACKUP plugged in; a copy of the data is made every night.',
      'The POS updates itself at night. Licence > Update now updates at once; a failed update puts the old version back by itself.',
      'If the screen says the POS is restarting, wait a minute; it comes back by itself.',
    ] },
    { id: 'support', title: 'Getting help', steps: [
      'The ? button and About show the support phone, WhatsApp, email and hours.',
      'Tell support the version number shown at the bottom of the screen.',
    ] },
  ],
  gu: [
    { id: 'order', title: 'ઓર્ડર લેવો', steps: [
      'POS ખોલો. ડાઇન-ઇન અને ટેબલ, અથવા ટેકઅવે અને ગ્રાહકનું નામ પસંદ કરો.',
      'વસ્તુ ઉમેરવા તેના પર ટેપ કરો. સાઇઝ કે વધારાનું હોય તો પહેલાં પૂછશે. + / - થી સંખ્યા બદલો, અથવા "જૈન" જેવી નોંધ ઉમેરો.',
      'ઓર્ડર આપો દબાવો. રસોડાની સ્ક્રીન પર તરત દેખાશે; કાગળની ટિકિટ માટે KOT પ્રિન્ટ દબાવો.',
    ] },
    { id: 'tables', title: 'ટેબલ અને ચાલુ ઓર્ડર', steps: [
      'ચાલુ ઓર્ડરવાળા ટેબલની યાદીમાં તેનો ટોકન દેખાય છે. તે પસંદ કરવાથી તમારી વસ્તુઓ એ જ ઓર્ડરમાં આગલા રાઉન્ડ તરીકે ઉમેરાય છે.',
      'ફક્ત નવો રાઉન્ડ જ રસોડામાં અને KOT પર જાય છે.',
      'ઓર્ડરમાં: ટેબલ બદલો થી ગ્રાહકોને ખાલી ટેબલ પર લઈ જાઓ, ટેબલ જોડો થી બે ટેબલનું એક બિલ, બિલ વહેંચો થી અમુક વસ્તુઓનું અલગ બિલ.',
    ] },
    { id: 'kitchen', title: 'રસોડાની સ્ક્રીન અને સ્ટેશન', steps: [
      'રસોડાના PC કે ટેબ્લેટ પર રસોડું ખોલો. નવા ઓર્ડર પોતે આવે છે; રાંધવાનું શરૂ, પછી તૈયાર દબાવો.',
      'મેનૂ પેજ પર સ્ટેશન (જેમ કે તંદૂર કે બાર) બનાવો તો દરેક સ્ક્રીન ફક્ત પોતાનું સ્ટેશન બતાવી શકે, અને દરેક સ્ટેશનનો અલગ KOT છપાય.',
      'TV પર ટોકન સ્ક્રીન બની રહેલા અને લઈ જવા તૈયાર ટોકન બતાવે છે.',
    ] },
    { id: 'bills', title: 'બિલ અને ચુકવણી', steps: [
      'ઓર્ડર પર બિલ દબાવો. જરૂર હોય તો ડિસ્કાઉન્ટ ઉમેરીને બિલ બનાવો.',
      'રોકડ, UPI (QR માં ચોક્કસ રકમ) અથવા કાર્ડ લો. બિલ ભાગમાં પણ ચૂકવી શકાય.',
      'ટિપ ઉમેરો વેઇટરની ટિપ નોંધે છે; તે બિલ કે GST માં નથી.',
      'છપાયેલું બિલ ફક્ત કારણ અને મેનેજર PIN થી જ રદ થઈ શકે.',
    ] },
    { id: 'qr', title: 'ટેબલ પર QR થી ઓર્ડર', steps: [
      'ટેબલ > QR કોડ: શીટ છાપો અને દરેક કોડ તેના ટેબલ પર મૂકો.',
      '"ગ્રાહક QR કોડથી ઓર્ડર કરી શકે" ચાલુ કરો; તેમના ઓર્ડર સીધા રસોડામાં એ ટેબલના ઓર્ડરમાં જાય છે.',
      'ગ્રાહક વેઇટર બોલાવો કે બિલ માંગો દબાવે ત્યારે POS પર સંદેશ આવે છે.',
      'કોડનો દુરુપયોગ થાય તો એ ટેબલ માટે નવો કોડ દબાવીને ફરી છાપો.',
    ] },
    { id: 'dayclose', title: 'દિવસ બંધ', steps: [
      'સવારે ગલ્લામાં રહેલી રોકડ સાથે શિફ્ટ ખોલો.',
      'ખર્ચ પેજ પર થયેલો ખર્ચ નોંધો; "ગલ્લામાંથી" હોય તો અપેક્ષિત રોકડ ઘટે છે.',
      'રાત્રે રોકડ ગણીને શિફ્ટ બંધ કરો. દિવસનો રિપોર્ટ (Z રિપોર્ટ) વેચાણ, GST, ચુકવણી, ટિપ અને ખર્ચ બતાવે છે; WhatsApp પર મોકલો સારાંશ મોકલે છે.',
    ] },
    { id: 'reports', title: 'રિપોર્ટ અને GST', steps: [
      'રિપોર્ટ કોઈ પણ દિવસોનું વેચાણ બતાવે છે: દિવસ, શ્રેણી, વસ્તુ, ચુકવણી, કલાક અને સ્ટાફ મુજબ.',
      'GST બિલ ડાઉનલોડ (CSV) તમારા એકાઉન્ટન્ટને દરેક બિલ કરપાત્ર રકમ, CGST અને SGST સાથે આપે છે.',
    ] },
    { id: 'shop', title: 'દુકાનનું PC: બેકઅપ અને અપડેટ', steps: [
      'POSBACKUP નામની USB ડ્રાઇવ લગાવેલી રાખો; દરરોજ રાત્રે ડેટાની નકલ બને છે.',
      'POS રાત્રે પોતે અપડેટ થાય છે. લાઇસન્સ > હમણાં અપડેટ કરો તરત અપડેટ કરે છે; અપડેટ નિષ્ફળ જાય તો જૂનું વર્ઝન પોતે પાછું આવે છે.',
      'સ્ક્રીન પર POS ફરી શરૂ થાય છે લખાય તો એક મિનિટ રાહ જુઓ; તે પોતે પાછું આવશે.',
    ] },
    { id: 'support', title: 'મદદ મેળવવી', steps: [
      '? બટન અને About માં સપોર્ટ ફોન, WhatsApp, ઇમેઇલ અને સમય દેખાય છે.',
      'સ્ક્રીનના નીચે દેખાતો વર્ઝન નંબર સપોર્ટને જણાવો.',
    ] },
  ],
  hi: [
    { id: 'order', title: 'ऑर्डर लें', steps: [
      'POS खोलें। यहीं खाना और टेबल चुनें, या पार्सल और ग्राहक का नाम।',
      'आइटम जोड़ने के लिए उन पर टैप करें। साइज़ या एक्स्ट्रा वाले आइटम पहले पूछते हैं। मात्रा बदलने के लिए + / - टैप करें, या "जैन" जैसा नोट जोड़ें।',
      'ऑर्डर भेजें दबाएँ। किचन स्क्रीन पर तुरंत दिखेगा; कागज़ की पर्ची चाहिए तो KOT प्रिंट करें दबाएँ।',
    ] },
    { id: 'tables', title: 'टेबल और चालू ऑर्डर', steps: [
      'जिस टेबल पर ऑर्डर चालू है, टेबल सूची में उसका टोकन दिखता है। उसे चुनने पर आपके आइटम उसी ऑर्डर में अगले राउंड के रूप में जुड़ते हैं।',
      'सिर्फ़ नया राउंड किचन और KOT पर जाता है।',
      'ऑर्डर में: टेबल बदलें से ग्राहक खाली टेबल पर जाते हैं, टेबल मिलाएँ से दो टेबल का एक बिल बनता है, बिल बाँटें से कुछ आइटम अलग बिल में जाते हैं।',
      'खाना टेबल पर पहुँचे तो खाना दे दिया दबाएँ, पार्सल ग्राहक को दें तो सौंप दिया। टोकन स्क्रीन से हट जाता है; टेबल बिल का भुगतान होने पर खाली होती है।',
    ] },
    { id: 'kitchen', title: 'किचन स्क्रीन और स्टेशन', steps: [
      'किचन के PC या टैबलेट पर किचन खोलें। नए ऑर्डर अपने आप आते हैं; बनाना शुरू करें, फिर तैयार करें टैप करें।',
      'मेनू पेज पर स्टेशन (जैसे तंदूर या बार) सेट हों तो हर स्क्रीन सिर्फ़ अपना स्टेशन दिखा सकती है, और हर स्टेशन का अलग KOT प्रिंट होता है।',
      'TV पर टोकन स्क्रीन बनते हुए और ले जाने के लिए तैयार टोकन दिखाती है।',
    ] },
    { id: 'bills', title: 'बिल और भुगतान', steps: [
      'ऑर्डर पर बिल दबाएँ। ज़रूरत हो तो छूट जोड़ें और बिल बनाएँ।',
      'नकद, UPI (QR में सही रकम दिखती है) या कार्ड लें। एक बिल का भुगतान हिस्सों में भी हो सकता है।',
      'टिप जोड़ें से वेटर की टिप दर्ज होती है; यह बिल या GST का हिस्सा नहीं है।',
      'प्रिंट हुआ बिल सिर्फ़ कारण और मैनेजर PIN से ही रद्द हो सकता है।',
    ] },
    { id: 'qr', title: 'टेबल पर QR ऑर्डरिंग', steps: [
      'टेबल > QR कोड: शीट प्रिंट करें और हर कोड उसकी टेबल पर लगाएँ।',
      'ग्राहकों को ऑर्डर करने देने के लिए "ग्राहक QR कोड से ऑर्डर कर सकते हैं" चालू करें; उनके ऑर्डर सीधे उस टेबल के चालू ऑर्डर में किचन जाते हैं।',
      'जब ग्राहक वेटर बुलाएँ या बिल माँगें दबाता है, POS पर संदेश दिखता है।',
      'किसी कोड का गलत इस्तेमाल हो तो उस टेबल का नया कोड दबाएँ और फिर से प्रिंट करें।',
    ] },
    { id: 'dayclose', title: 'दिन बंद', steps: [
      'सुबह दराज़ के नकद के साथ शिफ्ट शुरू करें।',
      'खर्च पेज पर खर्च दर्ज करें; "टिल से" होने पर होना चाहिए नकद कम होता है।',
      'रात को नकद गिनें और शिफ्ट बंद करें। दिन की रिपोर्ट (Z रिपोर्ट) बिक्री, GST, भुगतान, टिप और खर्च दिखाती है; WhatsApp पर शेयर करें से सारांश भेजा जाता है।',
    ] },
    { id: 'reports', title: 'रिपोर्ट और GST', steps: [
      'रिपोर्ट किसी भी दिनों की बिक्री दिखाती है: दिन, कैटेगरी, आइटम, भुगतान, घंटे और स्टाफ अनुसार।',
      'GST बिल डाउनलोड करें (CSV) से आपके अकाउंटेंट को हर बिल कर योग्य मूल्य, CGST और SGST के साथ मिलता है।',
    ] },
    { id: 'shop', title: 'दुकान का PC: बैकअप और अपडेट', steps: [
      'POSBACKUP नाम की USB ड्राइव लगी रहने दें; हर रात डेटा की कॉपी बनती है।',
      'POS रात को अपने आप अपडेट होता है। लाइसेंस > अभी अपडेट करें से तुरंत अपडेट होता है; अपडेट न चले तो पुराना वर्ज़न अपने आप वापस आता है।',
      'स्क्रीन पर POS रीस्टार्ट हो रहा है लिखा हो तो एक मिनट रुकें; यह अपने आप वापस आएगा।',
    ] },
    { id: 'support', title: 'मदद कैसे लें', steps: [
      '? बटन और जानकारी में सपोर्ट फ़ोन, WhatsApp, ईमेल और समय दिखते हैं।',
      'सपोर्ट को स्क्रीन के नीचे दिखा वर्ज़न नंबर बताएँ।',
    ] },
  ],
};
