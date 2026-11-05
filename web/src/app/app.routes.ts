import { Routes } from '@angular/router';
import { Home } from './home/home';
import { Settings } from './settings/settings';

export const routes: Routes = [
  { path: '', component: Home, title: 'FinanceGoals' },
  { path: 'settings', component: Settings, title: 'Settings · FinanceGoals' },
  { path: '**', redirectTo: '' },
];
