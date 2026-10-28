import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DocumentsApi } from '../core/documents-api';
import { formatBrl, formatPercent, statusLabel } from '../core/format';
import { LiveStatus } from '../core/live-status';
import { DocumentStatus, DocumentSummary, SampleScenario, SampleTemplate } from '../core/models';
import { StatusBadge } from '../shared/status-badge';

const FILTERS: (DocumentStatus | null)[] = [null, 'NeedsReview', 'Approved', 'Rejected'];

@Component({
  selector: 'app-documents-page',
  imports: [RouterLink, FormsModule, DatePipe, StatusBadge],
  templateUrl: './documents-page.html',
})
export class DocumentsPage {
  private readonly api = inject(DocumentsApi);
  protected readonly live = inject(LiveStatus);

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly filter = signal<DocumentStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly dragging = signal(false);
  protected readonly message = signal<string | null>(null);

  protected template: SampleTemplate | null = null;
  protected scenario: SampleScenario = 'Valid';

  protected readonly filters = FILTERS;
  protected readonly visible = computed(() => {
    const filter = this.filter();
    return filter ? this.documents().filter((d) => d.status === filter) : this.documents();
  });
  protected readonly counts = computed(() => {
    const counts = new Map<DocumentStatus | null, number>([[null, this.documents().length]]);
    for (const document of this.documents()) {
      counts.set(document.status, (counts.get(document.status) ?? 0) + 1);
    }
    return counts;
  });

  protected readonly formatBrl = formatBrl;
  protected readonly formatPercent = formatPercent;

  constructor() {
    this.load();
    effect(() => {
      if (this.live.lastChange()) {
        untracked(() => this.load());
      }
    });
  }

  protected filterLabel(status: DocumentStatus | null): string {
    return status ? statusLabel(status) : 'Todos';
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadFiles(input.files);
    input.value = '';
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.uploadFiles(event.dataTransfer?.files ?? null);
  }

  protected generateSample(): void {
    this.busy.set(true);
    this.api.generateSample(this.template, this.scenario).subscribe({
      next: () => this.done('Nota de exemplo gerada e enviada para o pipeline.'),
      error: () => this.done('Não foi possível gerar a nota de exemplo.'),
    });
  }

  private uploadFiles(files: FileList | null): void {
    const pdfs = Array.from(files ?? []).filter((f) => f.name.toLowerCase().endsWith('.pdf'));
    if (pdfs.length === 0) {
      this.message.set('Selecione arquivos PDF.');
      return;
    }
    this.busy.set(true);
    let pending = pdfs.length;
    for (const file of pdfs) {
      this.api.upload(file).subscribe({
        next: () => --pending === 0 && this.done(`${pdfs.length} arquivo(s) enviado(s).`),
        error: () => this.done(`Falha ao enviar ${file.name}.`),
      });
    }
  }

  private done(message: string): void {
    this.busy.set(false);
    this.message.set(message);
    this.load();
  }

  private load(): void {
    this.api.list().subscribe((documents) => this.documents.set(documents));
  }
}
