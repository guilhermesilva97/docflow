import { Component, computed, input } from '@angular/core';
import { statusLabel, statusTone } from '../core/format';
import { DocumentStatus } from '../core/models';

@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [attr.data-tone]="tone()">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<DocumentStatus>();
  protected readonly label = computed(() => statusLabel(this.status()));
  protected readonly tone = computed(() => statusTone(this.status()));
}
