import { TestBed } from '@angular/core/testing';

import { LibraryFileService } from './library-file.service';

describe('LibraryFileService', () => {
  let service: LibraryFileService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(LibraryFileService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
