import { of } from 'rxjs';
import { Sort } from '@angular/material/sort';

import { EMPTY_PART_FILTER } from '../types/part-filter';
import { OrderNumber } from '../types/order-number';
import { Part } from '../types/part';
import { PartNumber } from '../types/part-number';
import { OrderNumberService } from './order-number.service';
import { PartDataSource } from './part.dataSource';
import { PartNumberService } from './part-number.service';
import { PartService } from './part.service';

describe('PartDataSource', () => {
  const sort: Sort = { active: 'id', direction: 'asc' };

  const parts: Part[] = [
    { id: 1, name: 'Resistor', value: '10k', comment: '', keywords: 'passive', lifecycleStatus: 'active' },
    { id: 2, name: 'Capacitor', value: '100n', comment: 'decoupling', keywords: '', lifecycleStatus: 'eol' },
  ];

  const partNumbers: PartNumber[] = [
    {
      id: 10,
      partId: 1,
      manufacturerId: 5,
      manufacturerPartNumber: 'RC0603FR-0710KL',
      quantity: 1,
      packageType: '0603',
    },
    {
      id: 11,
      partId: 1,
      manufacturerId: 6,
      manufacturerPartNumber: 'CRCW060310K0FKEA',
      quantity: 1,
      packageType: '0603',
    },
    {
      id: 12,
      partId: 2,
      manufacturerId: 7,
      manufacturerPartNumber: 'CL10B104KB8NNNC',
      quantity: 1,
      packageType: '0603',
    },
  ];

  const orderNumbers: OrderNumber[] = [
    {
      id: 20,
      orderNumberValue: '311-10.0KHRCT-ND',
      partNumberId: 10,
      distributorId: 3,
      quantityMin: 1,
      quantitySteps: 1,
    },
    {
      id: 21,
      orderNumberValue: '1276-1003-1-ND',
      partNumberId: 12,
      distributorId: 4,
      quantityMin: 1,
      quantitySteps: 1,
    },
  ];

  function createSource(): PartDataSource {
    const partService = { getParts: () => of(parts) } as unknown as PartService;
    const partNumberService = { getPartNumbers: () => of(partNumbers) } as unknown as PartNumberService;
    const orderNumberService = { getOrderNumbers: () => of(orderNumbers) } as unknown as OrderNumberService;
    const source = new PartDataSource(partService, partNumberService, orderNumberService);
    source.loadParts(sort);
    return source;
  }

  function visibleIds(source: PartDataSource): number[] {
    let ids: number[] = [];
    source.connect().subscribe(rows => {
      ids = rows.map(row => row.id);
    }).unsubscribe();
    return ids;
  }

  it('finds a part by manufacturer part number', () => {
    const source = createSource();
    source.setFilter({ ...EMPTY_PART_FILTER, search: 'rc0603fr' });
    expect(visibleIds(source)).toEqual([1]);
  });

  it('finds a part by any of its manufacturer part numbers', () => {
    const source = createSource();
    source.setFilter({ ...EMPTY_PART_FILTER, search: 'CRCW0603' });
    expect(visibleIds(source)).toEqual([1]);
  });

  it('finds a part by distributor order number', () => {
    const source = createSource();
    source.setFilter({ ...EMPTY_PART_FILTER, search: '1276-1003' });
    expect(visibleIds(source)).toEqual([2]);
  });

  it('still matches name, value, comment, and keywords', () => {
    const source = createSource();
    source.setFilter({ ...EMPTY_PART_FILTER, search: 'decoupling' });
    expect(visibleIds(source)).toEqual([2]);
  });

  it('keeps the manufacturer filter', () => {
    const source = createSource();
    source.setFilter({ ...EMPTY_PART_FILTER, manufacturerId: 6 });
    expect(visibleIds(source)).toEqual([1]);
  });
});
