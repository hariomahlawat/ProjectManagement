// PRISM Collage Studio — the sticker catalogue (docs/ux/collage-studio.md).
//
// Three kinds of sticker, none from the Internet:
// - crests and logos an administrator uploaded to PRISM, given to setUnit() by the page;
// - drawn stickers (ribbon, rosette, stamps…), drawn here as SVG and recoloured at will;
// - emoji drawings from Twemoji (graphics CC BY 4.0, by Twitter, Inc. and other contributors),
//   a chosen set copied to wwwroot/img/collage/stickers/twemoji/ with their licence.
// Each sticker has search words. Nothing here touches the page or the network.
//
// A crest or logo is "unit:<id>". Its picture is PRISM's own PNG at /api/collage/stickers/<id>/image,
// under the app's path base (setBasePath, from the page's data-app-base); one that is no longer
// listed (retired, or not loaded yet) still draws from that address.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.PrismCollageStickers = api;
    // PRISM may be served under a sub-path; the page names it (Url.Content("~/")).
    api.setBasePath(root.document?.querySelector('[data-collage]')?.dataset?.appBase);
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // A table looked up by a name from a draft or a file: no prototype, so "constructor" or
  // "toString" is never a size, a design or a font.
  const lookupTable = (entries) => Object.freeze(Object.assign(Object.create(null), entries));

  function star(points, outer, inner, cx = 50, cy = 50) {
    const list = [];
    for (let i = 0; i < points * 2; i += 1) {
      const r = i % 2 ? inner : outer;
      const a = Math.PI * i / points - Math.PI / 2;
      list.push(`${(cx + r * Math.cos(a)).toFixed(1)} ${(cy + r * Math.sin(a)).toFixed(1)}`);
    }
    return list.join(' ');
  }

  const spokes = Array.from({ length: 24 }, (_, i) => {
    const a = i * Math.PI / 12;
    return `<line x1="50" y1="50" x2="${(50 + 42 * Math.cos(a)).toFixed(1)}" y2="${(50 + 42 * Math.sin(a)).toFixed(1)}" stroke="COLOUR" stroke-width="2"/>`;
  }).join('');

  const leaves = Array.from({ length: 6 }, (_, i) => {
    const y = 86 - i * 11;
    const xl = 22 + i * 1.5;
    const xr = 120 - xl;
    return `<ellipse cx="${xl}" cy="${y}" rx="10" ry="4.5" transform="rotate(${-40 - i * 8} ${xl} ${y})" fill="COLOUR"/>`
      + `<ellipse cx="${xr}" cy="${y}" rx="10" ry="4.5" transform="rotate(${40 + i * 8} ${xr} ${y})" fill="COLOUR"/>`;
  }).join('');

  /**
   * Drawn stickers: SVG with the word COLOUR where the chosen colour goes. `aspect` is
   * width / height; `colour` the colour a new one starts with.
   */
  const DRAWN = lookupTable({
    ribbon: Object.freeze({ label: 'Ribbon banner', words: 'ribbon banner title', aspect: 2.4, colour: '#d32f4f',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 100"><path d="M0 30h40v50H0l14-25z" fill="COLOUR" opacity=".7"/><path d="M240 30h-40v50h40l-14-25z" fill="COLOUR" opacity=".7"/><path d="M30 20h180v50H30z" fill="COLOUR"/><path d="M30 70l12 12V70zM210 70l-12 12V70z" fill="#000" opacity=".35"/></svg>' }),
    rosette: Object.freeze({ label: 'Rosette', words: 'rosette award badge prize medal', aspect: 0.8, colour: '#e2b13c',
      svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 80 100"><path d="M24 50l-12 46 16-8 10 12 4-44zM56 50l12 46-16-8-10 12-4-44z" fill="#c62f3a"/><polygon points="${star(18, 38, 33, 40, 40)}" fill="COLOUR"/><circle cx="40" cy="40" r="25" fill="#fff" opacity=".9"/><circle cx="40" cy="40" r="20" fill="COLOUR"/><polygon points="${star(5, 11, 4.5, 40, 40)}" fill="#fff"/></svg>` }),
    tape: Object.freeze({ label: 'Washi tape', words: 'tape washi strip', aspect: 3.4, colour: '#9fd3c7',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 170 50"><path d="M4 4h162l-5 7 5 7-5 7 5 7-5 7 5 7H4l5-7-5-7 5-7-5-7 5-7-5-7z" fill="COLOUR" opacity=".85"/><path d="M20 4l-10 42M50 4l-10 42M80 4l-10 42M110 4l-10 42M140 4l-10 42M168 4l-10 42" stroke="#fff" stroke-width="7" opacity=".4"/></svg>' }),
    bubble: Object.freeze({ label: 'Speech bubble', words: 'speech bubble talk say', aspect: 1.2, colour: '#172033',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 100"><path d="M20 8h80a15 15 0 0 1 15 15v38a15 15 0 0 1-15 15H55L30 96l5-20H20A15 15 0 0 1 5 61V23A15 15 0 0 1 20 8z" fill="#fff" stroke="COLOUR" stroke-width="4"/></svg>' }),
    arrow: Object.freeze({ label: 'Arrow doodle', words: 'arrow point doodle', aspect: 1.5, colour: '#172033',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 80"><path d="M10 62C28 22 66 10 104 30" fill="none" stroke="COLOUR" stroke-width="6" stroke-linecap="round"/><path d="M86 14l20 17-25 7" fill="none" stroke="COLOUR" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/></svg>' }),
    burst: Object.freeze({ label: 'Star burst', words: 'burst star sale new', aspect: 1, colour: '#ffcc33',
      svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><polygon points="${star(16, 49, 36)}" fill="COLOUR"/></svg>` }),
    sparkle: Object.freeze({ label: 'Sparkle', words: 'sparkle shine twinkle', aspect: 1, colour: '#ffd54f',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><path d="M50 0C55 35 65 45 100 50 65 55 55 65 50 100 45 65 35 55 0 50 35 45 45 35 50 0z" fill="COLOUR"/></svg>' }),
    heart: Object.freeze({ label: 'Heart', words: 'heart love', aspect: 1.1, colour: '#e53950',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 110 100"><path d="M55 96S5 64 5 32A25 25 0 0 1 55 22 25 25 0 0 1 105 32c0 32-50 64-50 64z" fill="COLOUR"/></svg>' }),
    wheel: Object.freeze({ label: 'Wheel', words: 'wheel chakra republic independence', aspect: 1, colour: '#0b2a8a',
      svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="50" r="44" fill="none" stroke="COLOUR" stroke-width="6"/><circle cx="50" cy="50" r="7" fill="COLOUR"/>${spokes}</svg>` }),
    laurel: Object.freeze({ label: 'Laurel', words: 'laurel wreath honour award', aspect: 1.2, colour: '#c9a227',
      svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 100"><path d="M60 94C28 90 14 64 16 30M60 94c32-4 46-30 44-64" fill="none" stroke="COLOUR" stroke-width="4"/>${leaves}</svg>` }),
    thanks: Object.freeze({ label: 'Stamp: Thank you', words: 'thank you thanks stamp', aspect: 2.3, colour: '#c62f3a',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 230 100"><rect x="6" y="6" width="218" height="88" rx="14" fill="none" stroke="COLOUR" stroke-width="6"/><rect x="16" y="16" width="198" height="68" rx="8" fill="none" stroke="COLOUR" stroke-width="2"/><text x="115" y="63" text-anchor="middle" font-family="Arial Black, Impact, sans-serif" font-weight="900" font-size="34" fill="COLOUR" textLength="176" lengthAdjust="spacingAndGlyphs">THANK YOU</text></svg>' }),
    wishes: Object.freeze({ label: 'Stamp: Best wishes', words: 'best wishes stamp farewell', aspect: 2.3, colour: '#1f6f43',
      svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 230 100"><rect x="6" y="6" width="218" height="88" rx="44" fill="none" stroke="COLOUR" stroke-width="6"/><text x="115" y="63" text-anchor="middle" font-family="Arial Black, Impact, sans-serif" font-weight="900" font-size="30" fill="COLOUR" textLength="180" lengthAdjust="spacingAndGlyphs">BEST WISHES</text></svg>' })
  });

  /** Emoji groups: the emoji and its search words. */
  const EMOJI_GROUPS = Object.freeze([
    ['celebration', 'Celebration', '🎉 party celebrate|🎊 confetti|🎈 balloon|🎁 gift present|🥳 party face|🎂 cake birthday|🍰 cake slice|🏆 trophy winner cup|🥇 gold medal first|🏅 medal sports|🎖️ medal military honour|👏 clap applause|🙌 hands raise|💐 bouquet flowers|🌹 rose|✨ sparkles shine|🌟 glowing star|⭐ star|🎀 bow ribbon|🕯️ candle|🥂 cheers toast|🎆 fireworks|🎗️ reminder ribbon|🎓 graduation cap'],
    ['picnic', 'Picnic and travel', '🏕️ camp camping|⛰️ mountain hill|🌄 sunrise|🚌 bus trip|🧺 basket picnic|☀️ sun sunny|🌳 tree|🌴 palm beach|🏖️ beach|⛺ tent|🚂 train|📸 camera photo|🗺️ map|🧭 compass|🌈 rainbow|🚣 boat rowing|🎒 bag backpack|🚗 car drive|✈️ plane flight|🚲 bicycle cycle'],
    ['food', 'Food', '🍉 watermelon|🍕 pizza|🍔 burger|🥪 sandwich|🍩 doughnut|☕ tea coffee|🍦 ice cream|🍿 popcorn|🥤 drink juice|🍎 apple|🍪 cookie biscuit|🥘 food pan|🍬 sweet candy|🥭 mango|🍵 chai tea|🍫 chocolate|🍌 banana|🍇 grapes'],
    ['sports', 'Sports', '⚽ football soccer|🏏 cricket|🏸 badminton|🏀 basketball|🏐 volleyball|🎯 target aim|🏃 run race|🚴 cycling race|🏊 swim|🥅 goal net|⛳ golf|🏋️ weights gym|🤸 gymnastics|🏑 hockey|🎾 tennis|🏓 table tennis|♟️ chess'],
    ['festival', 'Festival', '🪔 diya lamp diwali|🎇 sparkler|🏮 lantern|🌺 hibiscus flower|🪁 kite|🌼 marigold flower|🌸 blossom|🎶 music|🥁 drum|🪅 decoration|🇮🇳 india flag tricolour|🎑 moon festival'],
    ['nature', 'Nature', '🌻 sunflower|🍃 leaf|🍁 maple autumn|🌙 moon night|🦋 butterfly|🐦 bird|🌊 wave sea|🌲 pine tree|🍀 clover luck|🌷 tulip|🐾 paws pet|⛅ cloud|❄️ snow winter|🌞 sun face|🐘 elephant|🦚 peacock'],
    ['people', 'Faces and hands', '😀 smile happy|😂 laugh|😍 love eyes|🤩 star struck|😎 cool sunglasses|🥰 love|🤗 hug|👍 thumbs up|👋 wave bye|🙏 thanks namaste|💪 strong|❤️ heart love red|🧡 heart orange|💛 heart yellow|💚 heart green|💙 heart blue|💜 heart purple|😢 sad miss|🤝 handshake|👌 ok|✌️ peace victory|🫡 salute'],
    ['office', 'Office and events', '📅 calendar date|📜 certificate scroll|🎤 microphone speech|🏛️ building institution|📣 announcement|🔔 bell|📌 pin|✅ done tick|📝 note|💡 idea|🗓️ schedule|☎️ telephone|💻 computer laptop|📊 chart report']
  ].map(([key, label, list]) => Object.freeze({
    key,
    label,
    items: Object.freeze(list.split('|').map((entry) => {
      const space = entry.indexOf(' ');
      const emoji = entry.slice(0, space);
      return Object.freeze({ id: `tw:${codeOf(emoji)}`, emoji, words: `${entry.slice(space + 1)} ${label.toLowerCase()}` });
    }))
  })));

  /** The Twemoji file name of an emoji: its code points in hex, without the emoji style mark. */
  function codeOf(emoji) {
    return [...emoji].map((ch) => ch.codePointAt(0)).filter((cp) => cp !== 0xfe0f).map((cp) => cp.toString(16)).join('-');
  }

  const UNIT_PICTURE = /^\/api\/collage\/stickers\/[1-9][0-9]{0,8}\/image\?v=[1-9][0-9]{0,8}$/;

  // PRISM's own logos, shipped with PRISM and always offered first in Crests and logos
  // (docs/ux/invitations-posters.md, PR 4). The SDD logo is the 4× picture added in #3428.
  const LOGOS = lookupTable({
    sdd: Object.freeze({ label: 'SDD', file: '/img/logos/sdd-hd.webp', aspect: 1732 / 2940, words: 'sdd simulator development division archer' })
  });
  const logoItems = () => Object.freeze(Object.entries(LOGOS).map(([key, entry]) => Object.freeze({
    id: `logo:${key}`, label: entry.label, words: `${entry.label.toLowerCase()} ${entry.words} crest logo badge`
  })));
  let unit = Object.freeze([]);
  /**
   * The app's path base ("" at the site root, "/prism" under a sub-path), from the page's
   * data-app-base. Anything that is not a plain path is read as the site root.
   */
  function cleanBase(value) {
    const text = String(value || '').trim().replace(/\/+$/, '');
    return /^(?:\/[A-Za-z0-9._~%!$&'()*+,;=:@-]+)*$/.test(text) ? text : '';
  }

  let base = '';

  /** Sets the app's path base crest pictures are read under; returns it as used. */
  function setBasePath(value) {
    base = cleanBase(value);
    return base;
  }

  /**
   * The crests and logos PRISM listed, in picker order. Anything that is not a well-formed entry
   * with PRISM's own picture address is left out.
   */
  function setUnit(list) {
    const seen = new Set();
    unit = Object.freeze((Array.isArray(list) ? list : []).flatMap((entry) => {
      const id = Number(entry?.id);
      const name = String(entry?.name || '').trim().slice(0, 40);
      const width = Number(entry?.width);
      const height = Number(entry?.height);
      if (!Number.isInteger(id) || id < 1 || seen.has(id) || !name || !(width > 0) || !(height > 0)) return [];
      const below = typeof entry.url === 'string' && entry.url.startsWith(`${base}/`) ? entry.url.slice(base.length) : '';
      if (!UNIT_PICTURE.test(below) || !below.startsWith(`/api/collage/stickers/${id}/`)) return [];
      seen.add(id);
      return [Object.freeze({
        id: `unit:${id}`,
        label: name,
        url: entry.url,
        aspect: width / height,
        words: `${name} ${String(entry.words || '')} crest logo badge`.toLowerCase()
      })];
    }));
    return unit.length;
  }

  /** Every sticker the panel offers: crests and logos, drawn ones, then the emoji groups. */
  function groups() {
    const crests = Object.freeze({ key: 'crests', label: 'Crests and logos', items: Object.freeze([...logoItems(), ...unit]) });
    const drawn = Object.freeze({
      key: 'shapes',
      label: 'Shapes',
      items: Object.freeze(Object.entries(DRAWN).map(([key, entry]) => Object.freeze({ id: `draw:${key}`, words: `${entry.label.toLowerCase()} ${entry.words} shapes` })))
    });
    return [crests, drawn, ...EMOJI_GROUPS];
  }

  /** Stickers whose words contain every word typed, in catalogue order. */
  function search(query) {
    const words = String(query || '').toLowerCase().split(/\s+/).filter(Boolean);
    if (!words.length) return [];
    return groups().flatMap((group) => group.items).filter((item) => words.every((word) => item.words.includes(word)));
  }

  /** A sticker's entry: { kind: 'logo' | 'unit' | 'drawn' | 'emoji', ... }, or null when PRISM does not ship it. */
  function find(id) {
    if (typeof id !== 'string') return null;
    if (/^unit:[1-9][0-9]{0,8}$/.test(id)) {
      const listed = unit.find((entry) => entry.id === id);
      if (listed) return { kind: 'unit', ...listed };
      // Retired, or the list has not arrived: PRISM still serves the picture by its address.
      return { kind: 'unit', id, label: 'Crest or logo', url: `${base}/api/collage/stickers/${id.slice(5)}/image`, aspect: 1, words: '' };
    }
    if (id.startsWith('logo:')) {
      const entry = Object.prototype.hasOwnProperty.call(LOGOS, id.slice(5)) ? LOGOS[id.slice(5)] : null;
      return entry ? { kind: 'logo', id, key: id.slice(5), label: entry.label, url: `${base}${entry.file}`, aspect: entry.aspect, words: entry.words } : null;
    }
    if (id.startsWith('draw:')) {
      const entry = DRAWN[id.slice(5)];
      return entry ? { kind: 'drawn', key: id.slice(5), ...entry } : null;
    }
    for (const group of EMOJI_GROUPS) {
      const item = group.items.find((entry) => entry.id === id);
      if (item) return { kind: 'emoji', code: id.slice(3), aspect: 1, ...item };
    }
    return null;
  }

  /** A drawn sticker's SVG in a colour, ready to show as a picture. */
  function drawnSvg(key, colour) {
    const entry = DRAWN[key];
    const safe = /^#[0-9a-f]{6}$/i.test(colour) ? colour : entry.colour;
    return entry.svg.replace(/COLOUR/g, safe);
  }

  return Object.freeze({ DRAWN, EMOJI_GROUPS, LOGOS, groups, search, find, drawnSvg, codeOf, setUnit, setBasePath });
});
