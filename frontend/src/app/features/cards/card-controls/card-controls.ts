import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { Card } from '../../../core/models/card.models';
import { CardControls as CardControlsModel, ControlKey } from '../../../core/models/card-controls.models';
import { AuthService } from '../../../core/services/auth.service';
import { CardControlsService } from '../../../core/services/card-controls.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

interface ControlRow {
  key: ControlKey;
  name: string;
  description: string;
}

/**
 * Module 6: the cardholder switches channels on/off, sets daily limits and locks the card.
 * Admins see the same page read-only (customer support).
 */
@Component({
  selector: 'app-card-controls',
  imports: [CurrencyPipe, DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './card-controls.html'
})
export class CardControls implements OnInit {
  /** Bound from the route parameter :cardId (withComponentInputBinding). */
  readonly cardId = input.required<string>();

  private readonly service = inject(CardControlsService);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly auth = inject(AuthService);

  protected readonly currency = environment.currencyCode;
  protected readonly controls = signal<CardControlsModel | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  /** Admins only look; a blocked card's controls cannot be changed. */
  protected readonly readOnly = computed(() => this.auth.isAdmin() || this.controls()?.cardStatus !== 'Active');

  protected readonly rows: ControlRow[] = [
    { key: 'pos', name: 'Shop payments (POS)', description: 'Insert the card at a shop terminal and enter the PIN.' },
    { key: 'online', name: 'Online payments', description: 'Websites and apps (card not present).' },
    { key: 'contactless', name: 'Contactless (tap to pay)', description: 'Tap the card on the terminal.' },
    { key: 'atm', name: 'ATM withdrawals', description: 'Cash from ATMs. No cashback, cannot be converted to EMI.' },
    { key: 'international', name: 'International use', description: 'Use abroad, for every channel above that is on.' }
  ];

  protected readonly form = this.fb.group({
    pos: this.setting(),
    online: this.setting(),
    contactless: this.setting(),
    atm: this.setting(),
    international: this.setting()
  });

  ngOnInit(): void {
    this.load();
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.errors.set([]);
    this.message.set(null);
    this.service.update(this.id, this.form.getRawValue()).subscribe({
      next: c => {
        this.show(c);
        this.message.set('Saved. The new settings apply to the very next transaction.');
        this.busy.set(false);
      },
      error: e => {
        this.errors.set(apiErrorMessages(e));
        this.busy.set(false);
      }
    });
  }

  lock(): void {
    this.changeLock(this.service.lock(this.id), 'Card locked. All purchases are declined until you unlock it.');
  }

  unlock(): void {
    this.changeLock(this.service.unlock(this.id), 'Card unlocked. You can use it again.');
  }

  /** How much of today's limit is used, 0-100 (for the progress bar). */
  protected usedPercent(key: ControlKey): number {
    const c = this.controls()?.[key];
    return c?.dailyLimit ? Math.min(100, (c.spentToday / c.dailyLimit) * 100) : 0;
  }

  protected invalid(key: ControlKey): boolean {
    const limit = this.form.controls[key].controls.dailyLimit;
    return limit.touched && limit.invalid;
  }

  private load(): void {
    this.service.get(this.id).subscribe({
      next: c => this.show(c),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  private show(c: CardControlsModel): void {
    this.controls.set(c);
    for (const row of this.rows) {
      this.form.controls[row.key].setValue({ enabled: c[row.key].enabled, dailyLimit: c[row.key].dailyLimit });
    }
    if (this.readOnly()) this.form.disable();
    else this.form.enable();
  }

  private changeLock(request: Observable<Card>, done: string): void {
    this.errors.set([]);
    this.message.set(null);
    request.subscribe({
      next: () => {
        this.message.set(done);
        this.load();
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  private setting() {
    return this.fb.group({
      enabled: false,
      // Empty field = no extra limit. The API also checks it is not above the credit limit.
      dailyLimit: this.fb.control<number | null>(null, [Validators.min(1), Validators.max(1_000_000)])
    });
  }

  private get id(): number {
    return Number(this.cardId());
  }
}
