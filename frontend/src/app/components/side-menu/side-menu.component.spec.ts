import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { SideMenuComponent } from './side-menu.component';
import { AuthService } from '../../services/auth.service';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';

describe('SideMenuComponent', () => {
  let component: SideMenuComponent;
  let fixture: ComponentFixture<SideMenuComponent>;
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
    imports: [SideMenuComponent],
    providers: [provideRouter([]), provideHttpClient(withXhr(), withInterceptorsFromDi()), provideHttpClientTesting()]
})
    .compileComponents();

    fixture = TestBed.createComponent(SideMenuComponent);
    component = fixture.componentInstance;
    auth = TestBed.inject(AuthService);
    fixture.detectChanges();
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
    fixture.detectChanges();
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('hides the Administration heading when the user is not an admin', () => {
    signIn(false);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Main Menu');
    expect(text).toContain('Info');
    expect(text).not.toContain('Administration');
    expect(component.visibleGroups().map(group => group.label)).toEqual(['Main Menu']);
  });

  it('shows the Administration heading when the user is an admin', () => {
    signIn(true);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Administration');
    expect(text).toContain('User Management');
    expect(text).toContain('APIs');
    expect(text).toContain('Info');
  });
});
