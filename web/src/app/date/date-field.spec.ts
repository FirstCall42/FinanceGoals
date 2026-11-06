import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DateField, display, formatDate, parse } from './date-field';

@Component({
  imports: [DateField],
  template: `<app-date-field [(value)]="date" [max]="'2026-12-31'" />`,
})
class Host {
  readonly date = signal('2026-03-05');
}

function setup() {
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const element = fixture.nativeElement as HTMLElement;
  const text = element.querySelector('input[type=text]') as HTMLInputElement;
  const type = (value: string) => {
    text.value = value;
    text.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  return { fixture, element, text, type, host: fixture.componentInstance };
}

describe('Date field', () => {
  it('shows an ISO date as dd.MM.yyyy', () => {
    expect(setup().text.value).toBe('05.03.2026');
    expect(display('2026-01-31')).toBe('31.01.2026');
  });

  it('reads dd.MM.yyyy, with one-digit days and other separators, as an ISO date', () => {
    expect(parse('31.01.2026')).toBe('2026-01-31');
    expect(parse('5.3.2026')).toBe('2026-03-05');
    expect(parse('05/03/2026')).toBe('2026-03-05');
    expect(parse('29.02.2024')).toBe('2024-02-29');
  });

  it('rejects text that is not a real date', () => {
    expect(parse('31.02.2026')).toBe('');
    expect(parse('2026-03-05')).toBe('');
    expect(parse('5.3.26')).toBe('');
    expect(parse('')).toBe('');
  });

  it('passes the typed date to the form, and an invalid one as empty', () => {
    const { host, type, text } = setup();

    type('31.01.2026');
    expect(host.date()).toBe('2026-01-31');
    expect(text.getAttribute('aria-invalid')).toBeNull();

    type('31.02.2026');
    expect(host.date()).toBe('');
    expect(text.getAttribute('aria-invalid')).toBe('true');
  });

  it('shows every displayed date with zero-padded day and month', () => {
    expect(formatDate('2026-03-05')).toBe('05.03.2026');
    expect(formatDate('2026-11-07T12:00:00Z')).toBe('07.11.2026');
  });

  it('keeps text that is not a date, marked invalid, when leaving the field', () => {
    const { fixture, text, type } = setup();

    type('31.13.2030');
    text.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(text.value).toBe('31.13.2030');
    expect(text.getAttribute('aria-invalid')).toBe('true');
  });

  it('writes a valid date in full form when leaving the field', () => {
    const { fixture, text, type } = setup();

    type('7.4.2026');
    text.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(text.value).toBe('07.04.2026');
  });

  it('refuses a date after the latest allowed', () => {
    const { host, type, text } = setup();

    type('01.01.2027');

    expect(host.date()).toBe('');
    expect(text.value).toBe('01.01.2027');
    expect(text.getAttribute('aria-invalid')).toBe('true');
  });

  it('follows the form when the date is set from outside', () => {
    const { host, fixture, text } = setup();

    host.date.set('2030-12-24');
    fixture.detectChanges();

    expect(text.value).toBe('24.12.2030');
  });

  it('takes a date picked with the browser picker', () => {
    const { host, element, fixture } = setup();
    const native = element.querySelector('input[type=date]') as HTMLInputElement;

    native.value = '2026-07-04';
    native.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(host.date()).toBe('2026-07-04');
  });
});
