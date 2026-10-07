import { Component, OnInit, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { Card } from '../../../core/models/card.models';
import { PagedResult, Transaction } from '../../../core/models/transaction.models';
import { AuthService } from '../../../core/services/auth.service';
import { CardService } from '../../../core/services/card.service';
import { EmiService } from '../../../core/services/emi.service';
import { CardEmiSummary } from '../../../core/models/emi.models';
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
  private readonly emi = inject(EmiService);
  protected readonly auth = inject(AuthService);

  protected readonly currency = environment.currencyCode;
  protected readonly statusBadge = statusBadge;
  protected readonly signedAmount = signedAmount;

  protected readonly card = signal<Card | null>(null);
  protected readonly ledger = signal<PagedResult<Transaction> | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);
  /** Module 4: how much of the bill is repaid through EMI installments (cannot be paid via "Pay bill"). */
  protected readonly emiSummary = signal<CardEmiSummary | null>(null);
  protected payAmount = 0;

  ngOnInit(): void {
    this.cards.getCard(this.id).subscribe({
      next: c => this.card.set(c),
      error: e => this.errors.set(apiErrorMessages(e))
    });
    this.loadEmiSummary();
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
        this.loadEmiSummary();
        this.message.set(`Payment of ${r.transaction.amount} received. Thank you!`);
        this.loadPage(1);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  /** Amount "Pay bill" accepts: what is owed minus what is being repaid in EMIs. */
  payableNow(): number {
    return this.emiSummary()?.payableOutsideEmi ?? Math.max(0, this.card()?.outstandingAmount ?? 0);
  }

  private loadEmiSummary(): void {
    this.emi.getSummary(this.id).subscribe({
      next: s => {
        this.emiSummary.set(s);
        this.payAmount = s.payableOutsideEmi;
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  private get id(): number {
    return Number(this.cardId());
  }
}
