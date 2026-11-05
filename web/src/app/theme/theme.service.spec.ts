import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  it('follows the system until the user picks a theme', () => {
    TestBed.inject(ThemeService);

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('switches theme, applies it to the page and remembers it', () => {
    const theme = TestBed.inject(ThemeService);
    const before = theme.current();

    theme.toggle();

    const after = before === 'dark' ? 'light' : 'dark';
    expect(theme.current()).toBe(after);
    expect(document.documentElement.getAttribute('data-theme')).toBe(after);
    expect(localStorage.getItem('financegoals.theme')).toBe(after);
  });

  it('starts with a remembered pick', () => {
    localStorage.setItem('financegoals.theme', 'dark');

    const theme = TestBed.inject(ThemeService);

    expect(theme.current()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('ignores a stored value that is not a theme', () => {
    localStorage.setItem('financegoals.theme', 'purple');

    TestBed.inject(ThemeService);

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });
});
