import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommonModule } from '@angular/common';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogModule } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { FormsModule } from '@angular/forms';

import { BomTableComponent } from './bom-table.component';
import { BomLine } from '../../types/project';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';

describe('BomTableComponent', () => {
  let fixture: ComponentFixture<BomTableComponent>;

  const lines: BomLine[] = [
    line('R1', 'ok'),
    line('R2', 'eol'),
    line('R3', 'not_in_catalog'),
    line('R4', 'ambiguous'),
    line('R5', 'field_outdated'),
    line('R6', 'dnp', true)
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
    declarations: [BomTableComponent],
    imports: [CommonModule,
        FormsModule,
        MatTableModule,
        MatDialogModule],
    providers: [provideHttpClient(withXhr(), withInterceptorsFromDi()), provideHttpClientTesting()]
}).compileComponents();

    fixture = TestBed.createComponent(BomTableComponent);
    fixture.componentInstance.sha = 'abc';
    fixture.componentInstance.lines = lines;
    fixture.detectChanges();
  });

  it('renders a chip label for each catalog status', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('OK');
    expect(text).toContain('End of life');
    expect(text).toContain('Not in catalog');
    expect(text).toContain('Ambiguous');
    expect(text).toContain('Field outdated');
    expect(text).toContain('DNP');

    const chips = fixture.nativeElement.querySelectorAll('.bom-status');
    expect(chips.length).toBe(6);
  });
});

function line(reference: string, status: BomLine['status'], dnp = false): BomLine {
  return {
    reference,
    value: '10k',
    footprint: '',
    mpn: '',
    manufacturer: '',
    quantity: 1,
    status,
    dnp,
    partId: null,
    partName: '',
    outdatedFields: []
  };
}
