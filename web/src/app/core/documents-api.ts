import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DocumentDetail,
  DocumentStatus,
  DocumentSummary,
  Invoice,
  SampleScenario,
  SampleTemplate,
} from './models';

@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);

  list(status?: DocumentStatus): Observable<DocumentSummary[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<DocumentSummary[]>('/api/documents', { params });
  }

  get(id: string): Observable<DocumentDetail> {
    return this.http.get<DocumentDetail>(`/api/documents/${id}`);
  }

  upload(file: File): Observable<{ id: string }> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<{ id: string }>('/api/documents', body);
  }

  generateSample(
    template: SampleTemplate | null,
    scenario: SampleScenario,
  ): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/samples', { template, scenario });
  }

  approve(id: string, invoice: Invoice, reviewer: string): Observable<DocumentDetail> {
    return this.http.post<DocumentDetail>(`/api/documents/${id}/approve`, { invoice, reviewer });
  }

  reject(id: string, reason: string, reviewer: string): Observable<DocumentDetail> {
    return this.http.post<DocumentDetail>(`/api/documents/${id}/reject`, { reason, reviewer });
  }

  fileUrl(id: string): string {
    return `/api/documents/${id}/file`;
  }
}
