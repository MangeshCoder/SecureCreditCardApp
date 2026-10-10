import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { Card } from '../../../core/models/card.models';
import { CardControlsService } from '../../../core/services/card-controls.service';
import { CardService } from '../../../core/services/card.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

type Panel = { cardId: number; kind: 'pin' | 'reveal' } | null;

const PIN = [Validators.required, Validators.pattern(/^\d{4}$/)];

@Component({
  selector: 'app-my-cards',
  imports: [CurrencyPipe, DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './my-cards.html'
})
export class MyCards implements OnInit, OnDestroy {
  private readonly cardService = inject(CardService);
  private readonly controlsService = inject(CardControlsService);
  private readonly fb = inject(FormBuilder).nonNullable;
  private hideTimer?: ReturnType<typeof setTimeout>;

  protected readonly currency = environment.currencyCode;
  protected readonly cards = signal<Card[]>([]);
  protected readonly loading = signal(true);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly panel = signal<Panel>(null);
  /** Full card number, shown for 30 seconds after a successful PIN check. */
  protected readonly revealed = signal<{ cardId: number; number: string } | null>(null);

  protected readonly pinForm = this.fb.group({ currentPin: ['', PIN], newPin: ['', PIN] });
  protected readonly revealForm = this.fb.group({ pin: ['', PIN] });

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.hideTimer);
  }

  load(): void {
    this.loading.set(true);
    this.cardService.getMyCards().subscribe({
      next: cards => {
        this.cards.set(cards);
        this.loading.set(false);
      },
      error: e => {
        this.errors.set(apiErrorMessages(e));
        this.loading.set(false);
      }
    });
  }

  open(cardId: number, kind: 'pin' | 'reveal'): void {
    this.pinForm.reset();
    this.revealForm.reset();
    this.errors.set([]);
    this.message.set(null);
    this.panel.set({ cardId, kind });
  }

  close(): void {
    this.panel.set(null);
  }

  block(card: Card): void {
    if (!confirm(`Block card ${card.maskedCardNumber}? Only the bank can unblock it.`)) return;
    this.cardService.blockCard(card.cardId).subscribe({
      next: updated => {
        this.replace(updated);
        this.message.set(`Card ${updated.maskedCardNumber} has been blocked.`);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  /** Module 6: temporary lock - the cardholder can undo it, unlike Block. */
  toggleLock(card: Card): void {
    this.errors.set([]);
    const request = card.isLocked
      ? this.controlsService.unlock(card.cardId)
      : this.controlsService.lock(card.cardId);
    request.subscribe({
      next: updated => {
        this.replace(updated);
        this.message.set(updated.isLocked
          ? `Card ${updated.maskedCardNumber} is locked. Purchases are declined until you unlock it.`
          : `Card ${updated.maskedCardNumber} is unlocked.`);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  changePin(cardId: number): void {
    if (this.pinForm.invalid) {
      this.pinForm.markAllAsTouched();
      return;
    }
    this.cardService.changePin(cardId, this.pinForm.getRawValue()).subscribe({
      next: () => {
        this.close();
        this.message.set('PIN changed successfully.');
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  reveal(cardId: number): void {
    if (this.revealForm.invalid) {
      this.revealForm.markAllAsTouched();
      return;
    }
    this.cardService.revealCardNumber(cardId, this.revealForm.getRawValue().pin).subscribe({
      next: r => {
        this.close();
        this.revealed.set({ cardId: r.cardId, number: r.cardNumber.replace(/(\d{4})(?=\d)/g, '$1 ') });
        clearTimeout(this.hideTimer);
        this.hideTimer = setTimeout(() => this.revealed.set(null), 30_000);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  private replace(updated: Card): void {
    this.cards.update(list => list.map(c => (c.cardId === updated.cardId ? updated : c)));
  }
}
