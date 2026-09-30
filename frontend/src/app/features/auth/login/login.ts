import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html'
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.errors.set([]);
    this.auth.login(this.form.getRawValue()).subscribe({
      next: r => this.router.navigate([r.user.role === 'Admin' ? '/admin/cardholders' : '/cards']),
      error: e => {
        this.errors.set(apiErrorMessages(e));
        this.busy.set(false);
      }
    });
  }
}
