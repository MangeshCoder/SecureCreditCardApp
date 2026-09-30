import { Component, OnInit, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { Card } from '../../../core/models/card.models';
import { PagedResult, Transaction } from '../../../core/models/transaction.models';
import { AuthService } from '../../../core/services/auth.service';
import { CardService } from '../../../core/services/card.service';
import { TransactionService } from '../../../core/services/transaction.service';
import { apiErrorMessages } from '../../../core/utils/api-error';
import { signedAmount, statusBadge } from '../transaction-badges';

/** Card statement: balances, "Pay bill" (load) and the paged transaction ledger. */
@Component({
  selector: 'app-card-transactions',
  imports: [CurrencyPipe, DatePipe, FormsModule, RouterLink],
  templateUrl: './card-transactions.html'
})
export class CardTransactions implements OnInit {
  /** Bound from the route parameter :cardId (withComponentInputBinding). */
  readonly cardId = input.required<string>();

  private readonly cards = inject(CardService);
  private readonly transactions = inject(TransactionService);
  protected readonly auth = inject(AuthService);

  protected readonly currency = environment.currencyCode;
  protected readonly statusBadge = statusBadge;
  protected readonly signedAmount = signedAmount;

  protected readonly card = signal<Card | null>(null);
  protected readonly ledger = signal<PagedResult<Transaction> | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);
  protected payAmount = 0;

  ngOnInit(): void {
    this.cards.getCard(this.id).subscribe({
      next: c => {
        this.card.set(c);
        this.payAmount = Math.max(0, c.outstandingAmount);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
    this.loadPage(1);
  }

  loadPage(page: number): void {
    this.transactions.getForCard(this.id, page, 10).subscribe({
      next: r => this.ledger.set(r),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  payBill(): void {
    this.errors.set([]);
    this.message.set(null);
    this.transactions.load(this.id, this.payAmount).subscribe({
      next: r => {
        this.card.set(r.card);
        this.payAmount = Math.max(0, r.card.outstandingAmount);
        this.message.set(`Payment of ${r.transaction.amount} received. Thank you!`);
        this.loadPage(1);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  private get id(): number {
    return Number(this.cardId());
  }
}
