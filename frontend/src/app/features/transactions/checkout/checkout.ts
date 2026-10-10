import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';
import { MerchantCategory, SwipeResponse, TransactionChannel } from '../../../core/models/transaction.models';
import { TransactionService } from '../../../core/services/transaction.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

const CASH_MCC = '6011';

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

  /** Module 6: how the card is used - each one is a separate switch in Card controls. */
  protected readonly channels: { value: TransactionChannel; label: string }[] = [
    { value: 'Pos', label: 'Shop terminal (chip + PIN)' },
    { value: 'Online', label: 'Online checkout' },
    { value: 'Contactless', label: 'Tap to pay (contactless)' },
    { value: 'Atm', label: 'ATM cash withdrawal' }
  ];
  protected readonly countries = [
    { code: 'IN', name: 'India' },
    { code: 'US', name: 'United States' },
    { code: 'GB', name: 'United Kingdom' },
    { code: 'AE', name: 'United Arab Emirates' },
    { code: 'SG', name: 'Singapore' },
    { code: 'FR', name: 'France' }
  ];

  protected readonly form = inject(FormBuilder).nonNullable.group({
    merchantName: ['Big Bazaar', [Validators.required, Validators.maxLength(100)]],
    merchantCategoryCode: ['5411', Validators.required],
    channel: ['Pos' as TransactionChannel],
    merchantCountry: ['IN'],
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

  /** ATM withdrawals must use MCC 6011 (cash), and 6011 is only valid at an ATM. */
  channelChanged(): void {
    const v = this.form.getRawValue();
    if (v.channel === 'Atm' && v.merchantCategoryCode !== CASH_MCC) {
      this.form.patchValue({ merchantCategoryCode: CASH_MCC, merchantName: 'SBI ATM Andheri' });
    } else if (v.channel !== 'Atm' && v.merchantCategoryCode === CASH_MCC) {
      this.form.patchValue({ merchantCategoryCode: '5411', merchantName: 'Big Bazaar' });
    }
  }

  /** Decline reasons the cardholder can fix themselves under Card controls. */
  protected isControlDecline(reason: string | null): boolean {
    return !!reason && /cardholder|Daily limit/.test(reason);
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
      amount: v.amount,
      channel: v.channel,
      merchantCountry: v.merchantCountry
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
