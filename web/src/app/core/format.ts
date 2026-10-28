import { DocumentStatus, Invoice, InvoiceItem } from './models';

type Tone = 'neutral' | 'progress' | 'warning' | 'success' | 'danger';

const STATUS: Record<DocumentStatus, { label: string; tone: Tone }> = {
  Received: { label: 'Recebido', tone: 'neutral' },
  Extracting: { label: 'Extraindo', tone: 'progress' },
  Extracted: { label: 'Extraído', tone: 'progress' },
  Validating: { label: 'Validando', tone: 'progress' },
  NeedsReview: { label: 'Em revisão', tone: 'warning' },
  Approved: { label: 'Aprovado', tone: 'success' },
  Rejected: { label: 'Rejeitado', tone: 'danger' },
};

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const percent = new Intl.NumberFormat('pt-BR', { style: 'percent', maximumFractionDigits: 0 });

export function statusLabel(status: DocumentStatus): string {
  return STATUS[status].label;
}

export function statusTone(status: DocumentStatus): Tone {
  return STATUS[status].tone;
}

export function isFinal(status: DocumentStatus): boolean {
  return status === 'Approved' || status === 'Rejected';
}

export function formatBrl(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : brl.format(value);
}

export function formatPercent(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : percent.format(value);
}

export function formatCnpj(value: string | null | undefined): string {
  const digits = (value ?? '').replace(/\D/g, '');
  if (digits.length !== 14) {
    return value ?? '';
  }
  return `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8, 12)}-${digits.slice(12)}`;
}

export function recalculateItem(item: InvoiceItem): InvoiceItem {
  return { ...item, total: round2(item.quantity * item.unitPrice) };
}

export function sumItems(items: InvoiceItem[]): number {
  return round2(items.reduce((sum, item) => sum + item.total, 0));
}

export function normalizeInvoice(invoice: Invoice): Invoice {
  const blankToNull = (value: string | null) => (value?.trim() ? value.trim() : null);
  return {
    issuerName: blankToNull(invoice.issuerName),
    issuerCnpj: blankToNull(invoice.issuerCnpj)?.replace(/\D/g, '') ?? null,
    recipientName: blankToNull(invoice.recipientName),
    recipientCnpj: blankToNull(invoice.recipientCnpj)?.replace(/\D/g, '') ?? null,
    number: blankToNull(invoice.number),
    issueDate: blankToNull(invoice.issueDate),
    dueDate: blankToNull(invoice.dueDate),
    items: invoice.items
      .filter((item) => item.description.trim())
      .map((item) => recalculateItem({ ...item, description: item.description.trim() })),
    total: invoice.total === null ? null : round2(invoice.total),
  };
}

export function emptyInvoice(): Invoice {
  return {
    issuerName: null,
    issuerCnpj: null,
    recipientName: null,
    recipientCnpj: null,
    number: null,
    issueDate: null,
    dueDate: null,
    items: [],
    total: null,
  };
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}
