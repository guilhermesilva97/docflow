import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { DocumentsApi } from '../core/documents-api';
import {
  emptyInvoice,
  formatBrl,
  formatPercent,
  isFinal,
  normalizeInvoice,
  recalculateItem,
  statusLabel,
  sumItems,
} from '../core/format';
import { LiveStatus } from '../core/live-status';
import { DocumentDetail, Invoice, ValidationProblem } from '../core/models';
import { StatusBadge } from '../shared/status-badge';

@Component({
  selector: 'app-review-page',
  imports: [FormsModule, DatePipe, RouterLink, StatusBadge],
  templateUrl: './review-page.html',
})
export class ReviewPage {
  readonly id = input.required<string>();

  private readonly api = inject(DocumentsApi);
  private readonly live = inject(LiveStatus);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly document = signal<DocumentDetail | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly saving = signal(false);
  protected draft: Invoice = emptyInvoice();
  protected reviewer = 'revisor';
  protected rejectReason = '';

  protected readonly pdfUrl = computed(() =>
    this.sanitizer.bypassSecurityTrustResourceUrl(this.api.fileUrl(this.id())),
  );
  protected readonly editable = computed(() => this.document()?.status === 'NeedsReview');
  protected readonly processing = computed(() => {
    const status = this.document()?.status;
    return status !== undefined && status !== 'NeedsReview' && !isFinal(status);
  });

  protected readonly formatBrl = formatBrl;
  protected readonly formatPercent = formatPercent;
  protected readonly statusLabel = statusLabel;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
    effect(() => {
      const change = this.live.lastChange();
      if (change && change.documentId === untracked(() => this.id())) {
        untracked(() => this.load(change.documentId));
      }
    });
  }

  protected itemsSum(): number {
    return sumItems(this.draft.items.map(recalculateItem));
  }

  protected addItem(): void {
    this.draft.items = [
      ...this.draft.items,
      { description: '', quantity: 1, unitPrice: 0, total: 0 },
    ];
  }

  protected removeItem(index: number): void {
    this.draft.items = this.draft.items.filter((_, i) => i !== index);
  }

  protected useItemsSum(): void {
    this.draft.total = this.itemsSum();
  }

  protected approve(): void {
    this.saving.set(true);
    this.errors.set([]);
    this.api.approve(this.id(), normalizeInvoice(this.draft), this.reviewer).subscribe({
      next: (document) => this.loaded(document),
      error: (error: HttpErrorResponse) => this.failed(error),
    });
  }

  protected reject(): void {
    if (!this.rejectReason.trim()) {
      this.errors.set(['Informe o motivo da rejeição.']);
      return;
    }
    this.saving.set(true);
    this.api.reject(this.id(), this.rejectReason.trim(), this.reviewer).subscribe({
      next: (document) => this.loaded(document),
      error: (error: HttpErrorResponse) => this.failed(error),
    });
  }

  private load(id: string): void {
    this.api.get(id).subscribe((document) => this.loaded(document));
  }

  private loaded(document: DocumentDetail): void {
    this.saving.set(false);
    this.document.set(document);
    this.draft = structuredClone(document.invoice ?? emptyInvoice());
  }

  private failed(error: HttpErrorResponse): void {
    this.saving.set(false);
    const problem = error.error as ValidationProblem | string | null;
    if (problem && typeof problem === 'object' && problem.errors) {
      this.errors.set(Object.values(problem.errors).flat());
    } else {
      this.errors.set([
        typeof problem === 'string' ? problem : 'Não foi possível salvar a revisão.',
      ]);
    }
  }
}
