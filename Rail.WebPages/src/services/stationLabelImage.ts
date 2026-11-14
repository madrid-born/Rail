// A stretchable background keeps labels in MapLibre's collision and hit testing,
// so the border and Persian text move, hide and respond to clicks together.
export function createStationLabelImage(colors: string[]): ImageData {
  const canvas = document.createElement('canvas');
  canvas.width = 128;
  canvas.height = 64;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('Canvas is unavailable.');
  if (colors.length > 1) {
    const gradient = context.createLinearGradient(0, 0, canvas.width, 0);
    // Keep each line's color on its half, blending gently at the join.
    colors.forEach((color, index) => {
      gradient.addColorStop((index + 0.1) / colors.length, color);
      gradient.addColorStop((index + 0.9) / colors.length, color);
    });
    context.fillStyle = gradient;
  } else context.fillStyle = colors[0] ?? '#00636a';
  context.beginPath();
  context.roundRect(0, 0, 128, 64, 14);
  context.fill();
  // Cut out the center so the basemap remains visible inside the border.
  context.globalCompositeOperation = 'destination-out';
  context.fillStyle = '#000000';
  context.beginPath();
  context.roundRect(4, 4, 120, 56, 10);
  context.fill();
  return context.getImageData(0, 0, 128, 64);
}
