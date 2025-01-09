import data_loader as loader
import matplotlib
import matplotlib.ticker as ticker
from matplotlib import pyplot as plt

"""
matplotlib.use("pgf")
matplotlib.rcParams.update({
    "pgf.texsystem": "pdflatex",
    'font.family': 'serif',
    'text.usetex': True,
    'pgf.rcfonts': False,
})
"""

for runtime_data in loader.all_runtime_data:
    fig, axd = plt.subplot_mosaic([['topleft', 'topright'],['midleft', 'midright'],['bottom', 'bottom']])
    for key in runtime_data.keys():
        data = runtime_data[key]
        # naive
        naive_data = data[loader.pingpongKey]
        axd['topleft'].boxplot(naive_data)
        axd['topleft'].tick_params(axis='both', labelsize=12)
        axd['topleft'].set_xticks([1], ['Pingpong'])
        axd['topleft'].set_ylabel('Time (ms)', fontsize=12)

        axd['topright'].boxplot(data[loader.threadringKey])
        axd['topright'].tick_params(axis='both', labelsize=12)
        axd['topright'].set_xticks([1], ['Threadring'])
        axd['topright'].set_ylabel('Time (ms)', fontsize=12)

        axd['midleft'].boxplot(data[loader.bigKey])
        axd['midleft'].tick_params(axis='both', labelsize=12)
        axd['midleft'].set_xticks([1], ['Big'])
        axd['midleft'].set_ylabel('Time (ms)', fontsize=12)

        axd['midright'].boxplot(data[loader.bangKey])
        axd['midright'].tick_params(axis='both', labelsize=12)
        axd['midright'].set_xticks([1], ['Bang'])
        axd['midright'].set_ylabel('Time (ms)', fontsize=12)

        axd['bottom'].boxplot(data[loader.reverseBangKey])
        axd['bottom'].tick_params(axis='both', labelsize=12)
        axd['bottom'].set_xticks([1], ['Reversebang'])
        axd['bottom'].set_ylabel('Time (ms)', fontsize=12)

        for ax in axd.values():
            ax.yaxis.set_major_locator(ticker.MaxNLocator(nbins=4, min_n_ticks=4))

        plt.tight_layout()
        plt.savefig('plots/boxplots/' + key + '-boxplot.png', bbox_inches='tight')
           