import { TestBed } from '@angular/core/testing';

import { FootprintService } from './footprint.service';

describe('FootprintService', () => {
  let service: FootprintService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(FootprintService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
