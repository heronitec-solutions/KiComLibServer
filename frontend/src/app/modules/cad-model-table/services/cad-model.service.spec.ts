import { TestBed } from '@angular/core/testing';

import { CadModelService } from './cad-model.service';

describe('CadModelService', () => {
  let service: CadModelService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CadModelService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
