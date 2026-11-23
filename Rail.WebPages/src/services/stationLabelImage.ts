// A stretchable background keeps labels in MapLibre's collision and hit testing,
// so the border and Persian text move, hide and respond to clicks together.
export function createStationLabelImage(colors: string[], dashed = false): ImageData {
  const canvas = drawStationLabel(colors, dashed);
  return canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height);
}

export function stationLabelUrl(colors: string[], dashed = false): string {
  return drawStationLabel(colors, dashed).toDataURL();
}

function drawStationLabel(colors: string[], dashed: boolean): HTMLCanvasElement {
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
  if (dashed) {
    context.strokeStyle = context.fillStyle;
    context.lineWidth = 4;
    context.setLineDash([10, 7]);
    context.beginPath();
    context.roundRect(2, 2, 124, 60, 12);
    context.stroke();
    return canvas;
  }
  context.beginPath();
  context.roundRect(0, 0, 128, 64, 14);
  context.fill();
  // Cut out the center so the basemap remains visible inside the border.
  context.globalCompositeOperation = 'destination-out';
  context.fillStyle = '#000000';
  context.beginPath();
  context.roundRect(4, 4, 120, 56, 10);
  context.fill();
  return canvas;
}
