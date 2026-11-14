const dayMilliseconds = 86_400_000;
const formatter = new Intl.DateTimeFormat('en-u-ca-persian-nu-latn', {
  timeZone: 'UTC', year: 'numeric', month: '2-digit', day: '2-digit',
});
const months = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور', 'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'];
export const normalizeDateDigits = (value: string) => value.replace(/[۰-۹٠-٩]/g,
  digit => String('۰۱۲۳۴۵۶۷۸۹'.includes(digit) ? '۰۱۲۳۴۵۶۷۸۹'.indexOf(digit) : '٠١٢٣٤٥٦٧٨٩'.indexOf(digit)));

function parts(date: Date) {
  const values = formatter.formatToParts(date);
  const value = (type: string) => Number(values.find(part => part.type === type)?.value);
  return { year: value('year'), month: value('month'), day: value('day') };
}
function key(date: { year: number; month: number; day: number }) { return date.year * 10000 + date.month * 100 + date.day; }

export function gregorianToJalali(value: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return '';
  const date = new Date(`${value}T12:00:00Z`);
  if (!Number.isFinite(date.getTime()) || date.toISOString().slice(0, 10) !== value) return '';
  const { year, month, day } = parts(date);
  return `${year}/${String(month).padStart(2, '0')}/${String(day).padStart(2, '0')}`;
}

export function jalaliToGregorian(value: string): string | undefined {
  const match = /^(\d{4})[/-](\d{1,2})[/-](\d{1,2})$/.exec(normalizeDateDigits(value).trim());
  if (!match) return;
  const year = Number(match[1]), month = Number(match[2]), day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1 || day > 31) return;
  const target = key({ year, month, day });
  // Intl supplies the Persian calendar, including leap years. Search whole UTC
  // dates so neither conversion nor validation depends on the device time zone.
  let low = Date.UTC(1900, 0, 1) / dayMilliseconds;
  let high = Date.UTC(2200, 11, 31) / dayMilliseconds;
  while (low <= high) {
    const middle = Math.floor((low + high) / 2);
    const date = new Date(middle * dayMilliseconds);
    const candidate = key(parts(date));
    if (candidate === target) return date.toISOString().slice(0, 10);
    if (candidate < target) low = middle + 1; else high = middle - 1;
  }
}

export function jalaliMonth(value: string, offset = 0) {
  const date = new Date(`${value}T12:00:00Z`);
  const current = parts(date);
  const absolute = current.year * 12 + current.month - 1 + offset;
  const year = Math.floor(absolute / 12), month = absolute % 12 + 1;
  const first = jalaliToGregorian(`${year}/${month}/1`)!;
  const next = jalaliToGregorian(`${month === 12 ? year + 1 : year}/${month === 12 ? 1 : month + 1}/1`)!;
  const firstDate = new Date(`${first}T00:00:00Z`);
  const count = Math.round((Date.parse(`${next}T00:00:00Z`) - firstDate.getTime()) / dayMilliseconds);
  return { label: `${months[month - 1]} ${year.toLocaleString('fa-IR', { useGrouping: false })}`,
    first, weekday: (firstDate.getUTCDay() + 1) % 7,
    days: Array.from({ length: count }, (_, index) => ({ day: index + 1,
      date: new Date(firstDate.getTime() + index * dayMilliseconds).toISOString().slice(0, 10) })) };
}
