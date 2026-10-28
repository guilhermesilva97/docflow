import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./documents/documents-page').then((m) => m.DocumentsPage),
    title: 'DocFlow · Documentos',
  },
  {
    path: 'documents/:id',
    loadComponent: () => import('./review/review-page').then((m) => m.ReviewPage),
    title: 'DocFlow · Revisão',
  },
  { path: '**', redirectTo: '' },
];
