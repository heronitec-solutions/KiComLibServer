import { Component, Input, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter, startWith } from 'rxjs/operators';
import { AccountBoxComponent } from '../account-box/account-box.component';
import { LayoutService } from '../../services/layout.service';

/**
 * Page header used at the top of every routed page: sidebar trigger, tinted
 * icon tile, title/subtitle and a slot for page actions.
 */
@Component({
    selector: 'app-header',
    imports: [
    MatIconModule,
    AccountBoxComponent
],
    templateUrl: './app-header.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './app-header.component.scss'
})
export class AppHeaderComponent implements OnInit {
  /** Named `pageTitle` rather than `title` to avoid the native attribute. */
  @Input() pageTitle = '';
  @Input() subtitle = '';
  @Input() icon = 'inventory_2';

  /** Fallback used when a page does not pass an explicit title. */
  routeTitle = '';

  constructor(
    private router: Router,
    private activatedRoute: ActivatedRoute,
    private layoutService: LayoutService
  ) { }

  ngOnInit(): void {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        startWith(null)
      )
      .subscribe(() => {
        let currentRoute = this.activatedRoute;
        while (currentRoute.firstChild) {
          currentRoute = currentRoute.firstChild;
        }

        const routeData = currentRoute.snapshot.data;
        this.routeTitle = routeData?.['title'] ?? 'Welcome';
      });
  }

  toggleSidebar(): void {
    this.layoutService.toggleSidebar();
  }
}
