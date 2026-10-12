import { Component, DoCheck, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatAutocomplete, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';

interface SearchableOption {
  id: number;
  label: string;
}

@Component({
    selector: 'searchable-select',
    templateUrl: './searchable-select.component.html',
    styleUrl: './searchable-select.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class SearchableSelectComponent implements OnChanges, DoCheck {
  @Input() label = '';
  @Input() placeholder = '';
  @Input() ariaLabel = '';
  @Input() items: readonly unknown[] = [];
  @Input() idFn: (item: any) => number = item => item?.id;
  @Input() labelFn: (item: any) => string = item => String(item?.name ?? item?.filename ?? '');
  /** Re-read labels while the dialog is open, for example part-number rows being edited. */
  @Input() trackLabels = false;
  @Input() value: number | null = null;
  @Output() valueChange = new EventEmitter<number | null>();

  query = '';
  filtered: SearchableOption[] = [];
  readonly displayLabel = (option: SearchableOption | string | null): string => {
    if (option && typeof option === 'object') {
      return option.label;
    }

    return typeof option === 'string' ? option : '';
  };

  @ViewChild(MatAutocomplete) private autocomplete?: MatAutocomplete;
  private sorted: SearchableOption[] = [];
  private editing = false;
  private labelSignature = '';
  /** Blur runs on mousedown, before a click can select. Commit after the panel closes instead. */
  private commitAfterClose = false;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['items'] || changes['value'] || changes['labelFn'] || changes['idFn']) {
      this.rebuild();
    }
  }

  ngDoCheck(): void {
    if (!this.trackLabels) {
      return;
    }

    const signature = this.items.map(item => `${this.idFn(item)}\0${this.labelFn(item)}`).join('\n');
    if (signature !== this.labelSignature) {
      this.rebuild();
    }
  }

  onFocus(event: FocusEvent): void {
    this.editing = true;
    (event.target as HTMLInputElement).select();
    this.applyFilter();
  }

  onQueryChange(value: SearchableOption | string | null): void {
    if (value && typeof value === 'object') {
      this.pick(value.id);
      return;
    }

    this.query = value ?? '';
    this.editing = true;
    this.applyFilter();
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    const option = event.option.value as SearchableOption;
    this.pick(option.id);
  }

  onOptionMouseDown(event: MouseEvent): void {
    // Leave focus in the input. Otherwise the dialog focus trap and blur both run
    // before click, and the filtered option is gone by the time the click arrives.
    event.preventDefault();
  }

  onBlur(): void {
    if (this.autocomplete?.isOpen) {
      this.commitAfterClose = true;
      return;
    }

    this.commitAfterClose = false;
    this.commitQuery();
  }

  onPanelClosed(): void {
    if (!this.commitAfterClose) {
      return;
    }

    this.commitAfterClose = false;
    this.commitQuery();
  }

  private rebuild(): void {
    const options = this.items.map(item => ({ id: this.idFn(item), label: this.labelFn(item) }));
    options.sort((left, right) => left.label.localeCompare(right.label, undefined, { numeric: true, sensitivity: 'base' }));
    this.sorted = options;
    this.labelSignature = this.items.map(item => `${this.idFn(item)}\0${this.labelFn(item)}`).join('\n');
    if (!this.editing) {
      this.query = this.labelFor(this.value);
    }
    this.applyFilter();
  }

  private applyFilter(): void {
    const query = this.query.trim().toLowerCase();
    const selected = this.labelFor(this.value).trim().toLowerCase();
    const active = this.editing && query && query !== selected ? query : '';
    this.filtered = active
      ? this.sorted.filter(option => option.label.toLowerCase().includes(active))
      : this.sorted;
  }

  private commitQuery(): void {
    const typed = this.query.trim();
    const selectedLabel = this.labelFor(this.value);
    if (typed.toLowerCase() === selectedLabel.toLowerCase()) {
      this.query = selectedLabel;
      this.editing = false;
      this.applyFilter();
      return;
    }

    const exact = this.sorted.filter(option => option.label.toLowerCase() === typed.toLowerCase());
    if (exact.length === 1) {
      this.pick(exact[0].id);
      return;
    }

    if (!typed) {
      this.pick(null);
      return;
    }

    this.query = selectedLabel;
    this.editing = false;
    this.applyFilter();
  }

  private pick(id: number | null): void {
    this.editing = false;
    this.query = this.labelFor(id);
    this.applyFilter();
    if (id !== this.value) {
      this.value = id;
      this.valueChange.emit(id);
    }
  }

  private labelFor(id: number | null): string {
    if (id == null) {
      return '';
    }

    return this.sorted.find(option => option.id === id)?.label ?? '';
  }
}
