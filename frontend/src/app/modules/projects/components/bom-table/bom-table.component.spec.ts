import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommonModule } from '@angular/common';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogModule } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatTableModule } from '@angular/material/table';
import { FormsModule } from '@angular/forms';

import { BomTableComponent } from './bom-table.component';
import { BomLine } from '../../types/project';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';
import { tableCsv } from './bom-export';

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
        MatMenuModule,
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

  it('asks for a board count and shows the availability result', () => {
    const http = TestBed.inject(HttpTestingController);
    const input = fixture.nativeElement.querySelector('input[type="number"]') as HTMLInputElement;
    expect(input.value).toBe('1');
    fixture.componentInstance.projectId = 4;
    fixture.componentInstance.pieces = 2;
    fixture.componentInstance.lines = [line('R1', 'ok')];
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Check Availability');
    expect(fixture.nativeElement.textContent).toContain('for');
    expect(fixture.nativeElement.textContent).toContain('pieces');
    expect(fixture.nativeElement.textContent).toContain('Footprint');

    fixture.componentInstance.checkAvailability();
    const request = http.expectOne(call => call.url.includes('/bom/availability'));
    expect(request.request.body).toEqual({ pieces: 2, group: 'part' });
    request.flush({
      pieces: 2,
      warnings: [],
      lines: [{
        reference: 'R1',
        manufacturer: 'Vishay',
        mpn: 'CRCW080510K0FKEA',
        totalQuantity: 2,
        distributor: 'DigiKey',
        orderNumber: '541-10.0KHCT-ND',
        orderStatus: 'alternative_mpn',
        limited: true
      }]
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain('Footprint');
    expect(text).toContain('Manufacturer');
    expect(text).toContain('Vishay');
    expect(text).toContain('Total Qty');
    expect(text).toContain('DigiKey');
    expect(text).toContain('541-10.0KHCT-ND');
    expect(text).toContain('Alternative MPN');
    expect(text).toContain('Limited');
    http.verify();
  });

  it('quotes csv cells that contain commas', () => {
    expect(tableCsv(['Order-Number'], [['311-10K,CT']])).toContain('"311-10K,CT"');
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
