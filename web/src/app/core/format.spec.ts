import {
  formatBrl,
  formatCnpj,
  formatPercent,
  isFinal,
  normalizeInvoice,
  statusLabel,
  statusTone,
  sumItems,
} from './format';
import { emptyInvoice } from './format';

describe('format', () => {
  it('maps every status to a label and tone', () => {
    expect(statusLabel('NeedsReview')).toBe('Em revisão');
    expect(statusTone('Approved')).toBe('success');
    expect(statusTone('Validating')).toBe('progress');
    expect(isFinal('Rejected')).toBe(true);
    expect(isFinal('Extracting')).toBe(false);
  });

  it('formats money, percentages and CNPJ in pt-BR', () => {
    expect(formatBrl(1234.5).replace(/\s/g, ' ')).toBe('R$ 1.234,50');
    expect(formatBrl(null)).toBe('—');
    expect(formatPercent(0.87)).toBe('87%');
    expect(formatCnpj('11222333000181')).toBe('11.222.333/0001-81');
    expect(formatCnpj('123')).toBe('123');
  });

  it('normalizes a reviewed invoice before sending it to the API', () => {
    const invoice = normalizeInvoice({
      ...emptyInvoice(),
      issuerName: '  Aurora Tecnologia  ',
      issuerCnpj: '11.222.333/0001-81',
      number: ' ',
      items: [
        { description: ' Consultoria ', quantity: 3, unitPrice: 10.1, total: 0 },
        { description: '   ', quantity: 1, unitPrice: 1, total: 1 },
      ],
      total: 30.299999,
    });

    expect(invoice.issuerName).toBe('Aurora Tecnologia');
    expect(invoice.issuerCnpj).toBe('11222333000181');
    expect(invoice.number).toBeNull();
    expect(invoice.items).toEqual([
      { description: 'Consultoria', quantity: 3, unitPrice: 10.1, total: 30.3 },
    ]);
    expect(invoice.total).toBe(30.3);
    expect(sumItems(invoice.items)).toBe(30.3);
  });
});
