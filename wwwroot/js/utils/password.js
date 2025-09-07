// wwwroot/js/utils/password.js
export function generatePassword(length = 16) {
  const upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
  const lower = "abcdefghijklmnopqrstuvwxyz";
  const digits = "0123456789";
  const symbols = "!@#$%^&*()-_=+[]{};:,.<>?";
  const all = upper + lower + digits + symbols;

  if (!window.crypto || !window.crypto.getRandomValues) {
    throw new Error("Secure RNG not available");
  }

  const rnd = new Uint32Array(length);
  crypto.getRandomValues(rnd);

  let pwd = Array.from(rnd, n => all[n % all.length]).join("");

  const ensure = [
    [/[A-Z]/, upper],
    [/[a-z]/, lower],
    [/[0-9]/, digits],
    [/[^A-Za-z0-9]/, symbols],
  ];
  let i = 0;
  for (const [re, set] of ensure) {
    if (!re.test(pwd)) {
      const bytes = new Uint32Array(1);
      crypto.getRandomValues(bytes);
      const pos = i % pwd.length;
      const ch = set[bytes[0] % set.length];
      pwd = pwd.substring(0, pos) + ch + pwd.substring(pos + 1);
    }
    i++;
  }
  return pwd;
}

