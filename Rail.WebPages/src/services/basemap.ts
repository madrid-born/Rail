import type { ExpressionSpecification, StyleSpecification } from 'maplibre-gl';

export const basemapStyleUrl = 'https://tiles.openfreemap.org/styles/positron';
export const stationLabelFont = ['Noto Sans Regular'];

function usesPlaceName(value: unknown): boolean {
  if (typeof value === 'string') return /\{name(?::[^}]+|_[^}]+)?\}/.test(value);
  if (!Array.isArray(value)) return false;
  return (value[0] === 'get' && typeof value[1] === 'string' && /^name(?:[:_]|$)/.test(value[1]))
    || value.some(usesPlaceName);
}

// Prefer Persian, then the local name. Leave road numbers and other non-name
// symbols alone, and retain the provider's sources, glyphs and attribution.
export function localizeBasemap(style: StyleSpecification): StyleSpecification {
  const name: ExpressionSpecification = ['coalesce', ['get', 'name:fa'], ['get', 'name'],
    ['get', 'name:nonlatin'], ['get', 'name:latin']];
  return { ...style, layers: style.layers.map(layer => {
    if (layer.type !== 'symbol' || !usesPlaceName(layer.layout?.['text-field'])) return layer;
    return { ...layer, layout: { ...layer.layout, 'text-field': name } };
  }) };
}
