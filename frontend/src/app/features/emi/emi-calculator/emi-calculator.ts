import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';
import { EmiCalculation, EmiOption } from '../../../core/models/emi.models';
import { EmiService } from '../../../core/services/emi.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

/** EMI calculator: compare tenures for an amount, then see the full amortization schedule. */
@Component({
  selector: 'app-emi-calculator',
  imports: [ReactiveFormsModule, CurrencyPipe, DatePipe],
  templateUrl: './emi-calculator.html'
})
export class EmiCalculator implements OnInit {
  private readonly emi = inject(EmiService);

  protected readonly currency = environment.currencyCode;
  protected readonly options = signal<EmiOption[]>([]);
  protected readonly calculation = signal<EmiCalculation | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    amount: [50000, [Validators.required, Validators.min(1), Validators.max(1_000_000)]],
    tenureMonths: [12, Validators.required]
  });

  ngOnInit(): void {
    this.calculate();
  }

  calculate(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { amount, tenureMonths } = this.form.getRawValue();
    this.errors.set([]);
    this.emi.getOptions(amount).subscribe({
      next: o => this.options.set(o),
      error: e => this.errors.set(apiErrorMessages(e))
    });
    this.emi.previewEmiPlan(amount, Number(tenureMonths)).subscribe({
      next: c => this.calculation.set(c),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  choose(tenure: number): void {
    this.form.controls.tenureMonths.setValue(tenure);
    this.calculate();
  }
}
