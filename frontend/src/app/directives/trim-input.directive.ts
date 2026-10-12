import { Directive, ElementRef, HostListener, Injector, OnInit } from '@angular/core';
import { NgControl } from '@angular/forms';

const SKIPPED_TYPES = new Set([
  'password', 'number', 'checkbox', 'radio', 'file', 'hidden',
  'range', 'color', 'date', 'datetime-local', 'time', 'month', 'week'
]);

/** Drops leading and trailing whitespace when a text field loses focus. */
@Directive({
  selector: 'input[matInput], textarea[matInput], input.input-dark',
  standalone: true
})
export class TrimInputDirective implements OnInit {
  private control: NgControl | null = null;

  constructor(
    private element: ElementRef<HTMLInputElement | HTMLTextAreaElement>,
    private injector: Injector
  ) {}

  ngOnInit(): void {
    this.control = this.injector.get(NgControl, null);
  }

  @HostListener('blur')
  trim(): void {
    const input = this.element.nativeElement;
    if (input instanceof HTMLInputElement && SKIPPED_TYPES.has((input.type || 'text').toLowerCase())) {
      return;
    }

    const value = input.value;
    if (typeof value !== 'string') {
      return;
    }

    const trimmed = value.trim();
    if (trimmed === value) {
      return;
    }

    input.value = trimmed;
    this.control?.control?.setValue(trimmed);
  }
}
