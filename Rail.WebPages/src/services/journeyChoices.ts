import type { JourneyOption, WalkingJourneyResult } from './journey';

export function journeyChoices(response: WalkingJourneyResult): JourneyOption[] {
  // Keep the current route usable while an older development API is still running.
  const options = response.options?.length ? response.options : [{ ...response,
    id: response.id ?? 'current',
    totalSeconds: response.totalSeconds ?? Math.max(0, Math.ceil((Date.parse(`${response.arrivalLocal}+03:30`)
      - Date.parse(`${response.departureLocal}+03:30`)) / 1000)),
  }];
  return [...options].sort((a, b) => a.totalSeconds - b.totalSeconds).slice(0, 6);
}

export function selectedJourney(response: WalkingJourneyResult, id?: string): WalkingJourneyResult {
  const options = journeyChoices(response);
  return { ...response, ...(options.find(option => option.id === id) ?? options[0]) };
}
