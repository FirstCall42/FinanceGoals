import { Routes } from '@angular/router';
import { Home } from './home/home';
import { LoanCalculator } from './loans/loan-calculator';
import { InviteAccept } from './profile/invite-accept';
import { Settings } from './settings/settings';

export const routes: Routes = [
  { path: '', component: Home, title: 'FinanceGoals' },
  { path: 'calculator', component: LoanCalculator, title: 'Mortgage calculator · FinanceGoals' },
  { path: 'invite/:token', component: InviteAccept, title: 'Join a household · FinanceGoals' },
  { path: 'settings', component: Settings, title: 'Settings · FinanceGoals' },
  { path: '**', redirectTo: '' },
];
