import { TestBed } from '@angular/core/testing';

import { OrderNumberService } from './order-number.service';

describe('OrderNumberService', () => {
  let service: OrderNumberService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(OrderNumberService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
