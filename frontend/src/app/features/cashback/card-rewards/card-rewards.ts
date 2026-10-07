import { Component, OnInit, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { CashbackLog, CashbackRules, CashbackSummary } from '../../../core/models/cashback.models';
import { PagedResult } from '../../../core/models/transaction.models';
import { CashbackService } from '../../../core/services/cashback.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

/** Cashback dashboard for one card: totals, per-category breakdown, rules and the cashback ledger. */
@Component({
  selector: 'app-card-rewards',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './card-rewards.html'
})
export class CardRewards implements OnInit {
  /** Bound from the route parameter :cardId. */
  readonly cardId = input.required<string>();

  private readonly cashback = inject(CashbackService);

  protected readonly currency = environment.currencyCode;
  protected readonly summary = signal<CashbackSummary | null>(null);
  protected readonly rules = signal<CashbackRules | null>(null);
  protected readonly history = signal<PagedResult<CashbackLog> | null>(null);
  protected readonly errors = signal<string[]>([]);

  ngOnInit(): void {
    const id = Number(this.cardId());
    this.cashback.getSummary(id).subscribe({
      next: s => this.summary.set(s),
      error: e => this.errors.set(apiErrorMessages(e))
    });
    this.cashback.getRules().subscribe({
      next: r => this.rules.set(r),
      error: e => this.errors.set(apiErrorMessages(e))
    });
    this.loadPage(1);
  }

  loadPage(page: number): void {
    this.cashback.getHistory(Number(this.cardId()), page, 10).subscribe({
      next: h => this.history.set(h),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  /** Width of a category bar relative to the biggest category. */
  barWidth(amount: number): number {
    const max = Math.max(...(this.summary()?.byCategory.map(c => c.amount) ?? [0]), 0);
    return max > 0 ? Math.max(0, (amount / max) * 100) : 0;
  }
}
