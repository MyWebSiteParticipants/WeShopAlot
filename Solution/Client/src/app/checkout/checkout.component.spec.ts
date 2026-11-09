import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { of } from 'rxjs';
import { CheckoutComponent } from './checkout.component';
import { CheckoutModule } from './checkout.module';
import { AccountService } from '../account/account.service';
import { BasketService } from '../basket/basket.service';

describe('CheckoutComponent', () => {
  let component: CheckoutComponent;
  let fixture: ComponentFixture<CheckoutComponent>;

  const mockAccountService: any = {
    getUserAddress: () => of({
      firstName: 'Joy',
      lastName: 'Ghosh',
      street: '123 Ohio Ave',
      city: 'Youngstown',
      state: 'OH',
      zipcode: '44504'
    })
  };

  const mockBasketService: any = {
    getCurrentBasketValue: () => ({ deliveryMethodId: 2 })
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [CheckoutComponent],
      imports: [
        CheckoutModule,
        ReactiveFormsModule,
        RouterTestingModule,
        HttpClientTestingModule
      ],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AccountService, useValue: mockAccountService },
        { provide: BasketService, useValue: mockBasketService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CheckoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load the saved user address and delivery method into the form on startup', () => {
    expect(component.checkoutForm.get('addressForm.firstName')?.value).toBe('Joy');
    expect(component.checkoutForm.get('deliveryForm.deliveryMethod')?.value).toBe('2');
  });

  it('should enforce state and zipcode pattern validation rules', () => {
    const stateControl = component.checkoutForm.get('addressForm.state');
    const zipControl = component.checkoutForm.get('addressForm.zipcode');

    stateControl?.setValue('Ohio');
    zipControl?.setValue('abcde');
    expect(stateControl?.valid).toBeFalsy();
    expect(zipControl?.valid).toBeFalsy();

    stateControl?.setValue('OH');
    zipControl?.setValue('44504');
    expect(stateControl?.valid).toBeTruthy();
    expect(zipControl?.valid).toBeTruthy();
  });
});
