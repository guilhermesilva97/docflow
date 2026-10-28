import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { StatusChange } from './models';

export type LiveConnection = 'connecting' | 'online' | 'offline';

@Injectable({ providedIn: 'root' })
export class LiveStatus {
  readonly connection = signal<LiveConnection>('connecting');
  readonly lastChange = signal<StatusChange | null>(null);
  readonly feed = signal<StatusChange[]>([]);

  private readonly source = new EventSource('/api/documents/events');

  constructor() {
    this.source.onopen = () => this.connection.set('online');
    this.source.onerror = () => this.connection.set('offline');
    this.source.addEventListener('status', (event) => {
      const change = JSON.parse((event as MessageEvent<string>).data) as StatusChange;
      this.lastChange.set(change);
      this.feed.update((items) => [change, ...items].slice(0, 30));
    });
    inject(DestroyRef).onDestroy(() => this.source.close());
  }
}
