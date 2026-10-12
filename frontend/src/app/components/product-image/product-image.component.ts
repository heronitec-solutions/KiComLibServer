import { Component, Input, OnChanges, OnDestroy, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';

/**
 * Shows a product photo. API URLs are loaded with the bearer interceptor and
 * kept as a blob URL. blob: and data: URLs are used as given.
 */
@Component({
    selector: 'product-image',
    imports: [CommonModule],
    templateUrl: './product-image.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './product-image.component.scss'
})
export class ProductImageComponent implements OnChanges, OnDestroy {
  @Input() fetchUrl: string | null = null;
  @Input() alt = 'Product image';
  @Input() frameClass = '';

  src: string | null = null;
  settled = true;
  private ownedUrl: string | null = null;
  private requestUrl: string | null = null;

  constructor(private http: HttpClient) { }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['fetchUrl']) {
      this.load(this.fetchUrl);
    }
  }

  ngOnDestroy(): void {
    this.revoke();
  }

  private load(url: string | null): void {
    this.revoke();
    this.src = null;
    this.requestUrl = url;
    this.settled = !url;
    if (!url) {
      return;
    }

    if (url.startsWith('blob:') || url.startsWith('data:')) {
      this.src = url;
      this.settled = true;
      return;
    }

    this.http.get(url, { responseType: 'blob' }).subscribe({
      next: blob => {
        if (this.requestUrl !== url) {
          return;
        }

        this.ownedUrl = URL.createObjectURL(blob);
        this.src = this.ownedUrl;
        this.settled = true;
      },
      error: () => {
        if (this.requestUrl === url) {
          this.src = null;
          this.settled = true;
        }
      }
    });
  }

  private revoke(): void {
    if (this.ownedUrl) {
      URL.revokeObjectURL(this.ownedUrl);
      this.ownedUrl = null;
    }
  }
}
