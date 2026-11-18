// The support details and the version, loaded once and shared by the footer and the About page.
let pending = null;

export function loadAbout(force = false) {
  if (force || !pending) {
    const request = fetch('/api/about')
      .then((res) => (res.ok ? res.json() : null))
      .catch(() => null)
      .then((data) => {
        // A failed load is not kept, so the next screen tries again.
        if (!data && pending === request) pending = null;
        return data;
      });
    pending = request;
  }
  return pending;
}

// For tests.
export function clearAbout() {
  pending = null;
}

// "v1.7.2" for a release, the plain text for anything else (e.g. "dev").
export const versionText = (version) => (/^\d/.test(version || '') ? `v${version}` : version || '');

// tel: links need digits and an optional leading +.
export const telLink = (phone) => `tel:${String(phone || '').replace(/[^\d+]/g, '')}`;

// wa.me needs the country code and digits only; a 10-digit Indian number gets 91 in front.
export function whatsAppLink(number) {
  const digits = String(number || '').replace(/\D/g, '');
  if (digits.length === 0) return null;
  return `https://wa.me/${digits.length === 10 ? `91${digits}` : digits}`;
}
