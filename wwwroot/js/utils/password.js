// wwwroot/js/utils/password.js
export function generatePassword(length = 16, requiredUniqueChars = 1) {
  const upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
  const lower = "abcdefghijklmnopqrstuvwxyz";
  const digits = "0123456789";
  const symbols = "!@#$%^&*()-_=+[]{};:,.<>?";
  const all = upper + lower + digits + symbols;

  if (!window.crypto || !window.crypto.getRandomValues) {
    throw new Error("Secure RNG not available");
  }

  const safeLength = Math.max(4, Number.isFinite(length) ? Math.floor(length) : 16);
  const safeUnique = Math.min(
    all.length,
    safeLength,
    Math.max(1, Number.isFinite(requiredUniqueChars) ? Math.floor(requiredUniqueChars) : 1),
  );

  const randomIndex = (maximum) => {
    if (maximum <= 0) throw new RangeError("Maximum must be positive");
    const rejectionLimit = Math.floor(0x100000000 / maximum) * maximum;
    const bytes = new Uint32Array(1);
    do {
      crypto.getRandomValues(bytes);
    } while (bytes[0] >= rejectionLimit);
    return bytes[0] % maximum;
  };

  const characters = Array.from({ length: safeLength }, () => all[randomIndex(all.length)]);

  // Always generate a strong password that satisfies the standard Identity categories,
  // even where the current policy makes one of them optional.
  const requiredSets = [upper, lower, digits, symbols];
  requiredSets.forEach((set, index) => {
    if (!characters.some((character) => set.includes(character))) {
      characters[index % characters.length] = set[randomIndex(set.length)];
    }
  });

  // Identity can require a minimum number of distinct characters. Replace repeated
  // positions with unused characters until the configured requirement is met.
  const used = new Set(characters);
  if (used.size < safeUnique) {
    const unused = [...all].filter((character) => !used.has(character));
    for (let index = characters.length - 1; used.size < safeUnique && unused.length > 0; index -= 1) {
      const position = ((index % characters.length) + characters.length) % characters.length;
      const candidateIndex = randomIndex(unused.length);
      const candidate = unused.splice(candidateIndex, 1)[0];
      characters[position] = candidate;
      used.add(candidate);
    }
  }

  // Shuffle after injecting required character classes so their positions are not predictable.
  for (let index = characters.length - 1; index > 0; index -= 1) {
    const other = randomIndex(index + 1);
    [characters[index], characters[other]] = [characters[other], characters[index]];
  }

  return characters.join("");
}
