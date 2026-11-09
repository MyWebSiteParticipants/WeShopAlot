import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthGuard } from './auth.guard';
import { AccountService } from '../../account/account.service';

describe('AuthGuard', () => {
  let guard: AuthGuard;
  let navigatedUrl = '';

  const mockAccountService: any = {
    currentUser$: of(null)
  };

  const mockRouter: any = {
    navigate: (path: string[]) => { navigatedUrl = path[0]; }
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthGuard,
        { provide: AccountService, useValue: mockAccountService },
        { provide: Router, useValue: mockRouter }
      ]
    });
    guard = TestBed.inject(AuthGuard);
    navigatedUrl = '';
  });

  it('should allow access and return true if user is logged in', () => {
    return new Promise<void>((resolve) => {
      mockAccountService.currentUser$ = of({ email: 'test@test.com', token: 'jwt-token' });

      guard.canActivate({} as any, { url: '/checkout' } as any).subscribe(result => {
        expect(result).toBeTruthy();
        resolve();
      });
    });
  });

  it('should deny access, return false, and redirect to login if user is logged out', () => {
    return new Promise<void>((resolve) => {
      mockAccountService.currentUser$ = of(null);

      guard.canActivate({} as any, { url: '/checkout' } as any).subscribe(result => {
        expect(result).toBeFalsy();
        expect(navigatedUrl).toBe('/account/login');
        resolve();
      });
    });
  });
});
