import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';
import { MerchantCategory, SwipeResponse } from '../../../core/models/transaction.models';
import { TransactionService } from '../../../core/services/transaction.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

/**
 * Simulates a merchant POS terminal / online checkout. In production this request would come from
 * the merchant's acquiring bank (Module 5 adds signed inter-bank payloads), not from the cardholder's browser.
 */
@Component({
  selector: 'app-checkout',
  imports: [ReactiveFormsModule, CurrencyPipe],
  templateUrl: './checkout.html'
})
export class Checkout implements OnInit {
  private readonly transactions = inject(TransactionService);

  protected readonly currency = environment.currencyCode;
  protected readonly categories = signal<MerchantCategory[]>([]);
  protected readonly result = signal<SwipeResponse | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    merchantName: ['Big Bazaar', [Validators.required, Validators.maxLength(100)]],
    merchantCategoryCode: ['5411', Validators.required],
    amount: [1000, [Validators.required, Validators.min(0.01), Validators.max(1_000_000)]],
    cardNumber: ['', [Validators.required, Validators.pattern(/^\d{4} ?\d{4} ?\d{4} ?\d{4}$/)]],
    expiry: ['', [Validators.required, Validators.pattern(/^(0[1-9]|1[0-2])\/\d{2}$/)]],
    cvv: ['', [Validators.required, Validators.pattern(/^\d{3}$/)]],
    pin: ['', [Validators.required, Validators.pattern(/^\d{4}$/)]]
  });

  ngOnInit(): void {
    this.transactions.getMerchantCategories().subscribe({
      next: c => this.categories.set(c),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  invalid(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return c.touched && c.invalid;
  }

  pay(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const [mm, yy] = v.expiry.split('/');

    this.busy.set(true);
    this.errors.set([]);
    this.result.set(null);
    this.transactions.swipe({
      cardNumber: v.cardNumber.replace(/\s/g, ''),
      expiryMonth: Number(mm),
      expiryYear: 2000 + Number(yy),
      cvv: v.cvv,
      pin: v.pin,
      merchantName: v.merchantName,
      merchantCategoryCode: v.merchantCategoryCode,
      amount: v.amount
    }).subscribe({
      next: r => {
        this.result.set(r);
        this.busy.set(false);
        // Never keep card secrets in the form after the attempt.
        this.form.patchValue({ cvv: '', pin: '' });
        this.form.controls.cvv.markAsUntouched();
        this.form.controls.pin.markAsUntouched();
      },
      error: e => {
        this.errors.set(apiErrorMessages(e));
        this.busy.set(false);
      }
    });
  }
}
