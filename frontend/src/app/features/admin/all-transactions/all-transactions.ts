import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { PagedResult, Transaction } from '../../../core/models/transaction.models';
import { TransactionService } from '../../../core/services/transaction.service';
import { apiErrorMessages } from '../../../core/utils/api-error';
import { signedAmount, statusBadge } from '../../transactions/transaction-badges';

/** Back office: every transaction on every card, with merchant refunds. */
@Component({
  selector: 'app-all-transactions',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './all-transactions.html'
})
export class AllTransactions implements OnInit {
  private readonly transactions = inject(TransactionService);

  protected readonly currency = environment.currencyCode;
  protected readonly statusBadge = statusBadge;
  protected readonly signedAmount = signedAmount;
  protected readonly ledger = signal<PagedResult<Transaction> | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);

  ngOnInit(): void {
    this.loadPage(1);
  }

  loadPage(page: number): void {
    this.transactions.getAll(page, 20).subscribe({
      next: r => this.ledger.set(r),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  refund(t: Transaction): void {
    if (!confirm(`Refund ${t.amount} to card ${t.maskedCardNumber} for "${t.merchantName}"?`)) return;
    this.errors.set([]);
    this.transactions.refund(t.transactionId).subscribe({
      next: r => {
        this.message.set(`Refunded transaction #${t.transactionId}. Card available balance: ${r.card.availableBalance}.`);
        this.loadPage(this.ledger()?.page ?? 1);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }
}
