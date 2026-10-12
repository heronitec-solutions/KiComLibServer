import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { AccountBoxComponent } from './account-box.component';
import { AuthService } from '../../services/auth.service';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';

describe('AccountBoxComponent', () => {
  let component: AccountBoxComponent;
  let fixture: ComponentFixture<AccountBoxComponent>;
  let auth: AuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
    imports: [AccountBoxComponent, RouterTestingModule],
    providers: [provideHttpClient(withXhr(), withInterceptorsFromDi()), provideHttpClientTesting()]
})
    .compileComponents();
    
    fixture = TestBed.createComponent(AccountBoxComponent);
    component = fixture.componentInstance;
    auth = TestBed.inject(AuthService);
    fixture.detectChanges();
  });

  function signIn(canRead: boolean, canWrite: boolean, canReadWarehouse: boolean, canWriteWarehouse: boolean): void {
    auth.profile.set({
      authenticated: true,
      username: 'store',
      isAdmin: false,
      canRead,
      canWrite,
      canReadWarehouse,
      canWriteWarehouse,
      groups: []
    });
    fixture.detectChanges();
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('hides the read-only badge when warehouse write is granted', () => {
    signIn(true, false, true, true);

    expect(fixture.nativeElement.querySelector('.badge')).toBeNull();
    expect(component.catalogAccess()).toBe('Read only');
    expect(component.warehouseAccess()).toBe('Read and write');
  });

  it('shows the read-only badge when neither catalog nor warehouse can be changed', () => {
    signIn(true, false, true, false);

    expect(fixture.nativeElement.querySelector('.badge')?.textContent).toContain('Read only');
    expect(component.catalogAccess()).toBe('Read only');
    expect(component.warehouseAccess()).toBe('Read only');
  });
});
