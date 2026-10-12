import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { apiErrorMessage, AuthService } from '../../services/auth.service';
import { TrimInputDirective } from '../../directives/trim-input.directive';

@Component({
    selector: 'login-page',
    imports: [FormsModule, TrimInputDirective],
    changeDetection: ChangeDetectionStrategy.Eager,
    templateUrl: './login-page.component.html'
})
export class LoginPageComponent implements OnInit {
  username = '';
  password = '';
  error = '';
  expired = false;
  isSubmitting = false;

  constructor(
    private auth: AuthService,
    private router: Router,
    private route: ActivatedRoute
  ) { }

  ngOnInit(): void {
    this.expired = this.route.snapshot.queryParamMap.get('expired') === '1';
    this.auth.ensureSession().subscribe(profile => {
      if (profile.authenticated) {
        void this.router.navigateByUrl(this.afterLoginUrl());
      }
    });
  }

  submit(): void {
    if (this.isSubmitting || !this.username.trim() || !this.password) {
      return;
    }

    this.error = '';
    this.isSubmitting = true;
    this.auth.login(this.username.trim(), this.password).subscribe({
      next: () => {
        void this.router.navigateByUrl(this.afterLoginUrl());
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Sign-in failed');
        this.isSubmitting = false;
      }
    });
  }

  private afterLoginUrl(): string {
    if (this.auth.profile().mustChangePassword) {
      return '/account';
    }
    return this.safeReturnUrl();
  }

  private safeReturnUrl(): string {
    const value = this.route.snapshot.queryParamMap.get('returnUrl') || '/home';
    if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/login')) {
      return '/home';
    }
    return value;
  }
}
