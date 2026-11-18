export function itineraryRailStyle(outgoing: string, incoming?: string) {
  const safe = (color: string) => /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(color) ? color : '#000000';
  const bottom = safe(outgoing);
  const top = incoming ? safe(incoming) : bottom;
  return {
    '--step-line': bottom,
    '--step-dot': top === bottom ? bottom : `linear-gradient(to bottom, ${top} 0%, ${top} 50%, ${bottom} 50%, ${bottom} 100%)`,
  };
}
