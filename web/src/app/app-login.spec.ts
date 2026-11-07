import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './auth/auth.service';
import { routes } from './app.routes';

describe('App before and after sign-in', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController)
      .match(() => true)
      .forEach((request) => request.flush({}));
  });

  it('shows only the login page on a page that needs an account while signed out', async () => {
    await TestBed.inject(Router).navigateByUrl('/net-worth');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelector('.login h1')?.textContent).toContain('Sign in to FinanceGoals');
    expect(page.querySelector('.login app-sign-in')).not.toBeNull();
    expect(page.querySelector('nav')).toBeNull();
    expect(page.querySelector('router-outlet')).toBeNull();
    expect(page.querySelector('app-currency-switcher')).toBeNull();
  });

  it('leaves the calculator open without an account', async () => {
    await TestBed.inject(Router).navigateByUrl('/calculator');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelector('.login')).toBeNull();
    expect(page.querySelector('.topbar app-sign-in')).not.toBeNull();
  });

  it('shows the app once signed in', async () => {
    await TestBed.inject(Router).navigateByUrl('/net-worth');
    const auth = TestBed.inject(AuthService);
    auth.user.set({ id: 1, email: 'a@example.com', name: 'A' });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelector('.login')).toBeNull();
    expect(page.querySelector('nav')).not.toBeNull();
    expect(page.querySelector('.topbar app-sign-in')).not.toBeNull();
  });
});
