import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { InfoPageComponent } from './info-page.component';
import { AuthService } from '../../services/auth.service';

describe('InfoPageComponent', () => {
  let fixture: ComponentFixture<InfoPageComponent>;
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InfoPageComponent],
      providers: [provideRouter([]), provideHttpClient(withXhr(), withInterceptorsFromDi()), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(InfoPageComponent);
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/auth/me').flush({ authenticated: false });
  });

  afterEach(() => {
    http.verify();
  });

  function signIn(isAdmin: boolean): void {
    auth.profile.set({
      authenticated: true,
      username: isAdmin ? 'admin' : 'user',
      isAdmin,
      canRead: true,
      canWrite: isAdmin,
      canReadWarehouse: false,
      canWriteWarehouse: false,
      groups: []
    });
  }

  it('shows the license to every user and hides versions from non-admins', () => {
    signIn(false);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Copyright (C) 2026 Heronitec Solutions GmbH');
    expect(text).toContain('GNU General Public License');
    expect(text).toContain('info@heronitec-solutions.de');
    expect(text).not.toContain('Database schema');
    http.expectNone('/api/settings');
  });

  it('shows the settings version fields to an administrator', () => {
    signIn(true);
    fixture.detectChanges();

    const request = http.expectOne('/api/settings');
    request.flush({
      version: '1.4.0',
      httpLibraryVersion: '1.2',
      apiVersion: 3,
      schemaVersion: 18,
      runner: { reachable: true, kicad: '10.0.1' }
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('1.4.0');
    expect(text).toContain('1.2');
    expect(text).toContain('3');
    expect(text).toContain('18');
    expect(text).toContain('10.0.1');
    expect(text).toContain('Copyright (C) 2026 Heronitec Solutions GmbH');
  });
});
