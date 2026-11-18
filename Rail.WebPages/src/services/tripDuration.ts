export function tripDuration(seconds: number): string {
  const totalMinutes = Math.max(0, Math.ceil(seconds / 60));
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const parts: string[] = [];
  if (hours) parts.push(`${hours.toLocaleString('fa-IR')} ساعت`);
  if (minutes || !hours) parts.push(`${minutes.toLocaleString('fa-IR')} دقیقه`);
  return parts.join(' و ');
}
