export type DocumentStatus =
  'Received' | 'Extracting' | 'Extracted' | 'Validating' | 'NeedsReview' | 'Approved' | 'Rejected';

export type SampleTemplate = 'Classic' | 'Modern' | 'Compact';
export type SampleScenario = 'Valid' | 'InvalidCnpj' | 'WrongTotal';

export interface InvoiceItem {
  description: string;
  quantity: number;
  unitPrice: number;
  total: number;
}

export interface Invoice {
  issuerName: string | null;
  issuerCnpj: string | null;
  recipientName: string | null;
  recipientCnpj: string | null;
  number: string | null;
  issueDate: string | null;
  dueDate: string | null;
  items: InvoiceItem[];
  total: number | null;
}

export interface ValidationIssue {
  code: string;
  message: string;
}

export interface HistoryEntry {
  status: DocumentStatus;
  at: string;
  detail: string;
}

export interface DocumentSummary {
  id: string;
  fileName: string;
  status: DocumentStatus;
  receivedAt: string;
  updatedAt: string;
  issuerName: string | null;
  total: number | null;
  confidence: number | null;
}

export interface DocumentDetail {
  id: string;
  fileName: string;
  status: DocumentStatus;
  receivedAt: string;
  updatedAt: string;
  invoice: Invoice | null;
  confidence: number | null;
  extractionMethod: string | null;
  reviewedBy: string | null;
  issues: ValidationIssue[];
  history: HistoryEntry[];
}

export interface StatusChange {
  documentId: string;
  fileName: string;
  status: DocumentStatus;
  detail: string;
  occurredAt: string;
}

export interface ValidationProblem {
  title: string;
  errors: Record<string, string[]>;
}
