import type { InjectionKey, ShallowRef } from 'vue';

export interface PageHeadingState { owner: symbol; title: string; color?: string }
export const pageHeadingKey: InjectionKey<ShallowRef<PageHeadingState | undefined>> = Symbol('page-heading');
