export const pages = [
  { name: 'map', path: '/map', title: 'نقشه', icon: 'map' },
  { name: 'stations', path: '/stations', title: 'ایستگاه‌ها', icon: 'stations' },
  { name: 'saved', path: '/saved', title: 'ذخیره‌شده‌ها', icon: 'saved' },
  { name: 'settings', path: '/settings', title: 'تنظیمات', icon: 'settings' },
] as const;

export type PageName = typeof pages[number]['name'];
