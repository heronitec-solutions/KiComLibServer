import { TestBed } from '@angular/core/testing';

import { PartNumberService } from './part-number.service';

describe('PartNumberService', () => {
  let service: PartNumberService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(PartNumberService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
